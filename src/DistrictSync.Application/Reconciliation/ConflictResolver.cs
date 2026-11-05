using System.Text.Json;
using System.Text.Json.Nodes;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Rules;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Rules;

namespace DistrictSync.Application.Reconciliation;

public enum WinningSide
{
    None = 0,
    Left = 1,
    Right = 2,
}

/// <summary>What should happen to make the two systems agree.</summary>
public sealed record ResolutionOutcome(WinningSide Winner, WriteBackAction Action, Guid? TargetSourceSystemId, string PayloadJson, string Explanation)
{
    public static ResolutionOutcome Undecidable(string reason) => new(WinningSide.None, WriteBackAction.NoOp, null, "{}", reason);
}

/// <summary>
/// Pure decision logic for conflicting updates from different systems. Given a strategy and the two
/// observations, decides which side is authoritative and what must be written where. No I/O, fully unit-testable.
/// </summary>
public static class ConflictResolver
{
    public static ResolutionOutcome Resolve(ResolutionStrategy strategy, DiscrepancyContext ctx)
    {
        var d = ctx.Discrepancy;
        var winner = PickWinner(strategy, ctx);
        if (winner == WinningSide.None)
        {
            return ResolutionOutcome.Undecidable($"Strategy {strategy} could not pick a side (missing timestamps or equal trust).");
        }

        var (winningRecord, losingSource) = winner == WinningSide.Left
            ? (ctx.LeftRecord, ctx.RightSource)
            : (ctx.RightRecord, ctx.LeftSource);

        switch (d.Type)
        {
            case DiscrepancyType.FieldConflict:
            {
                if (winningRecord is null)
                {
                    return ResolutionOutcome.Undecidable("Winning side has no record.");
                }

                var patch = new JsonObject();
                var winningPayload = JsonNode.Parse(winningRecord.PayloadJson) as JsonObject ?? new JsonObject();
                foreach (var diff in d.Differences)
                {
                    patch[diff.Field] = winningPayload[diff.Field]?.DeepClone() ?? (winner == WinningSide.Left ? diff.LeftValue : diff.RightValue);
                }

                return new ResolutionOutcome(winner, WriteBackAction.UpdateFields, losingSource.Id, patch.ToJsonString(), $"{strategy}: {Describe(winner, ctx)} wins; update {d.Differences.Count} field(s) in {losingSource.Code}.");
            }

            case DiscrepancyType.MissingInLeft:
            case DiscrepancyType.MissingInRight:
            {
                var missingSide = d.Type == DiscrepancyType.MissingInLeft ? WinningSide.Left : WinningSide.Right;
                var presentRecord = missingSide == WinningSide.Left ? ctx.RightRecord : ctx.LeftRecord;
                var missingSource = missingSide == WinningSide.Left ? ctx.LeftSource : ctx.RightSource;
                var presentSource = missingSide == WinningSide.Left ? ctx.RightSource : ctx.LeftSource;

                if (winner == missingSide)
                {
                    // The system that lacks the record is authoritative: the record should not exist elsewhere either.
                    return new ResolutionOutcome(winner, WriteBackAction.DeleteRecord, presentSource.Id, presentRecord?.PayloadJson ?? "{}", $"{strategy}: {missingSource.Code} is authoritative and has no record; delete from {presentSource.Code}.");
                }

                return new ResolutionOutcome(winner, WriteBackAction.CreateRecord, missingSource.Id, presentRecord?.PayloadJson ?? "{}", $"{strategy}: {presentSource.Code} is authoritative; create record in {missingSource.Code}.");
            }

            case DiscrepancyType.Duplicate:
                return ResolutionOutcome.Undecidable("Duplicates within one source need a human to choose the surviving record.");
            default:
                return ResolutionOutcome.Undecidable($"Unsupported discrepancy type {d.Type}.");
        }
    }

    /// <summary>Builds the outcome for a manually specified set of values (from an approved case proposal).</summary>
    public static ResolutionOutcome Manual(DiscrepancyContext ctx, string manualValuesJson, Guid targetSourceSystemId)
    {
        var target = targetSourceSystemId == ctx.LeftSource.Id ? ctx.LeftSource : ctx.RightSource;
        var action = ctx.Discrepancy.Type switch
        {
            DiscrepancyType.MissingInLeft when target.Id == ctx.LeftSource.Id => WriteBackAction.CreateRecord,
            DiscrepancyType.MissingInRight when target.Id == ctx.RightSource.Id => WriteBackAction.CreateRecord,
            _ => WriteBackAction.UpdateFields,
        };
        return new ResolutionOutcome(WinningSide.None, action, target.Id, manualValuesJson, $"Manual values approved; apply to {target.Code}.");
    }

    public static WinningSide PickWinner(ResolutionStrategy strategy, DiscrepancyContext ctx)
    {
        switch (strategy)
        {
            case ResolutionStrategy.PreferLeft:
                return WinningSide.Left;
            case ResolutionStrategy.PreferRight:
                return WinningSide.Right;
            case ResolutionStrategy.PreferHigherTrust:
                if (ctx.LeftSource.TrustRank == ctx.RightSource.TrustRank)
                {
                    return WinningSide.None;
                }

                return ctx.LeftSource.TrustRank > ctx.RightSource.TrustRank ? WinningSide.Left : WinningSide.Right;
            case ResolutionStrategy.PreferNewest:
            {
                var l = ctx.LeftRecord?.SourceUpdatedAt;
                var r = ctx.RightRecord?.SourceUpdatedAt;
                if (l is null && r is null)
                {
                    return WinningSide.None;
                }

                if (l is null)
                {
                    return WinningSide.Right;
                }

                if (r is null)
                {
                    return WinningSide.Left;
                }

                if (l == r)
                {
                    // Same timestamp: fall back to trust so the decision is still deterministic.
                    return PickWinner(ResolutionStrategy.PreferHigherTrust, ctx);
                }

                return l > r ? WinningSide.Left : WinningSide.Right;
            }

            default:
                return WinningSide.None;
        }
    }

    private static string Describe(WinningSide side, DiscrepancyContext ctx) => side == WinningSide.Left ? ctx.LeftSource.Code : ctx.RightSource.Code;

    public static string Serialize(ResolutionOutcome outcome) => JsonSerializer.Serialize(outcome, Application.Common.JsonDefaults.Options);
}
