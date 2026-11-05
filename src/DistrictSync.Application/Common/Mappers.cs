using System.Text.Json;
using System.Text.Json.Nodes;
using DistrictSync.Application.Messaging;
using DistrictSync.Contracts.Api;
using DistrictSync.Contracts.Events;
using DistrictSync.Domain.Cases;
using DistrictSync.Domain.Ingestion;
using DistrictSync.Domain.Reconciliation;
using DistrictSync.Domain.Rules;
using DistrictSync.Domain.Sources;
using DistrictSync.Domain.Tenants;
using DistrictSync.Domain.Workflows;

namespace DistrictSync.Application.Common;

/// <summary>Entity → DTO projections. Kept in one place so the API surface stays consistent.</summary>
public static class Mappers
{
    public static TenantDto ToDto(Tenant t) => new(t.Id, t.Name, t.Slug, t.IsActive, t.Quotas.MaxConcurrentReconciliations, t.Quotas.MaxConcurrentIngestions, t.Quotas.IngestionRequestsPerMinute, t.CreatedAt);

    public static SourceSystemDto ToDto(SourceSystem s) => new(s.Id, s.Name, s.Code, s.Kind.ToString(), s.TrustRank, s.ConfigJson, s.IsEnabled, s.ApiKeyHash is not null, s.CreatedAt);

    public static IngestionBatchDto ToDto(IngestionBatch b, IReadOnlyDictionary<Guid, string> sourceCodes) => new(
        b.Id, b.SourceSystemId, Code(sourceCodes, b.SourceSystemId), b.EntityType, b.Status.ToString(), b.FileName, b.SizeBytes,
        b.TotalRecords, b.AcceptedRecords, b.RejectedRecords, b.DuplicateRecords, b.ReceivedAt, b.StartedAt, b.CompletedAt, b.FailureReason, b.SubmittedBy);

    public static ReconciliationStatsDto ToDto(ReconciliationStats s) => new(s.LeftRecords, s.RightRecords, s.Matched, s.MissingInLeft, s.MissingInRight, s.FieldConflicts, s.Duplicates, s.AutoResolved, s.CasesOpened, s.Ignored);

    public static ReconciliationRunDto ToDto(ReconciliationRun r, IReadOnlyDictionary<Guid, string> sourceCodes) => new(
        r.Id, r.EntityType, r.LeftSourceSystemId, Code(sourceCodes, r.LeftSourceSystemId), r.RightSourceSystemId, Code(sourceCodes, r.RightSourceSystemId),
        r.Status.ToString(), ToDto(r.Stats), r.RuleSetId, r.RuleSetVersion, r.RequestedAt, r.StartedAt, r.CompletedAt, r.FailureReason, r.RequestedBy, r.Checkpoint);

    public static DiscrepancyDto ToDto(Discrepancy d) => new(
        d.Id, d.RunId, d.EntityType, d.Type.ToString(), d.Status.ToString(), d.Severity.ToString(), d.MatchKey, d.LeftSourceSystemId, d.RightSourceSystemId,
        d.LeftRecordId, d.RightRecordId, d.Differences.Select(x => new FieldDifferenceDto(x.Field, x.LeftValue, x.RightValue, x.IsCritical)).ToList(),
        d.AppliedRuleId, d.ResolutionNote, d.DetectedAt, d.ResolvedAt);

    public static SourceRecordDto ToDto(SourceRecord r) => new(r.Id, r.SourceSystemId, r.ExternalId, r.MatchKey, Attributes(r.PayloadJson), r.SourceUpdatedAt, r.IngestedAt, r.IsDeleted);

    public static ComparisonProfileDto ToDto(ComparisonProfile p) => new(p.Id, p.Name, p.EntityType, p.Fields.Select(f => new ComparedFieldDto(f.Name, f.Normalizer.ToString(), f.IsCritical)).ToList(), p.IsDefault, p.CreatedAt);

    public static RuleActionDto ToDto(RuleAction a) => new(a.Kind.ToString(), a.Strategy?.ToString(), a.Priority?.ToString(), a.AssignToRole, a.Note);

    public static RuleDto ToDto(Rule r) => new(r.Id, r.Name, r.Description, r.Order, r.Condition, ToDto(r.Action), r.IsEnabled);

    public static RuleSetDto ToDto(RuleSet s) => new(s.Id, s.FamilyId, s.Name, s.EntityType, s.Version, s.Status.ToString(), s.Rules.Select(ToDto).ToList(), s.CreatedAt, s.PublishedAt, s.PublishedBy, s.ChangeNote);

    public static RuleSetSummaryDto ToSummary(RuleSet s) => new(s.Id, s.FamilyId, s.Name, s.EntityType, s.Version, s.Status.ToString(), s.Rules.Count, s.CreatedAt, s.PublishedAt);

    public static CaseDto ToDto(ResolutionCase c, DateTimeOffset now) => new(
        c.Id, c.DiscrepancyId, c.RunId, c.Title, c.Priority.ToString(), c.State.ToString(), c.AssignedRole, c.AssigneeId, c.EscalationLevel, c.DueAt, c.OpenedAt, c.ClosedAt,
        !c.IsTerminal && c.DueAt < now, ETag.From(c.RowVersion));

    public static CaseHistoryEntryDto ToDto(CaseHistoryEntry h) => new(h.At, h.Actor, h.Action, h.FromState, h.ToState, h.Note);

    public static ProposedResolutionDto? ToDto(ProposedResolution? p) => p is null ? null : new(p.Strategy?.ToString(), p.ManualValuesJson, p.Note, p.ProposedBy, p.ProposedAt);

    public static DeadLetterDto ToDto(DeadLetterMessage m) => new(m.Id, m.Queue, m.RoutingKey, m.MessageType, m.Reason, m.Attempts, m.DeadLetteredAt, m.ReplayedAt, m.Payload);

    public static WorkflowInstanceDto ToDto(WorkflowInstance w) => new(w.Id, w.WorkflowType, w.SubjectId, w.Status.ToString(), w.WakeUpAt, w.LockOwner, w.LockExpiresAt, w.Attempts, w.LastError, w.UpdatedAt);

    public static RuleAction ToDomain(RuleActionDto dto)
    {
        if (!Enum.TryParse<RuleActionKind>(dto.Kind, true, out var kind))
        {
            throw ValidationException.For("action.kind", $"Unknown action kind '{dto.Kind}'. Expected AutoResolve, OpenCase or Ignore.");
        }

        ResolutionStrategy? strategy = null;
        if (dto.Strategy is not null)
        {
            if (!Enum.TryParse<ResolutionStrategy>(dto.Strategy, true, out var s))
            {
                throw ValidationException.For("action.strategy", $"Unknown strategy '{dto.Strategy}'.");
            }

            strategy = s;
        }

        CasePriority? priority = null;
        if (dto.Priority is not null)
        {
            if (!Enum.TryParse<CasePriority>(dto.Priority, true, out var p))
            {
                throw ValidationException.For("action.priority", $"Unknown priority '{dto.Priority}'.");
            }

            priority = p;
        }

        if (kind == RuleActionKind.AutoResolve && strategy is null)
        {
            throw ValidationException.For("action.strategy", "AutoResolve requires a strategy.");
        }

        return new RuleAction(kind, strategy, priority, dto.AssignToRole, dto.Note);
    }

    public static Dictionary<string, string?> Attributes(string payloadJson)
    {
        try
        {
            if (JsonNode.Parse(payloadJson) is JsonObject obj)
            {
                return obj.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    private static string Code(IReadOnlyDictionary<Guid, string> codes, Guid id) => codes.TryGetValue(id, out var code) ? code : "?";
}
