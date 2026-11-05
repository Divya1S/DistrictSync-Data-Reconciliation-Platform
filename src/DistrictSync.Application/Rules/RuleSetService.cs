using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Application.Reconciliation;
using DistrictSync.Application.Rules.Dsl;
using DistrictSync.Contracts.Api;
using DistrictSync.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace DistrictSync.Application.Rules;

public sealed class RuleSetService(IAppDbContext db, ITenantContext tenant, RuleEngine engine, TimeProvider clock)
{
    public async Task<IReadOnlyList<RuleSetSummaryDto>> ListAsync(CancellationToken ct) =>
        (await db.RuleSets.AsNoTracking().Include(r => r.Rules).OrderBy(r => r.Name).ThenByDescending(r => r.Version).ToListAsync(ct)).Select(Mappers.ToSummary).ToList();

    public async Task<RuleSetDto> GetAsync(Guid id, CancellationToken ct) => Mappers.ToDto(await LoadAsync(id, tracking: false, ct));

    public async Task<RuleSet> CreateDraftAsync(CreateRuleSetRequest request, CancellationToken ct)
    {
        var set = RuleSet.CreateDraft(tenant.TenantId, request.Name, request.EntityType, clock.GetUtcNow());
        db.RuleSets.Add(set);
        await db.SaveChangesAsync(ct);
        return set;
    }

    public async Task<Rule> AddRuleAsync(Guid ruleSetId, UpsertRuleRequest request, CancellationToken ct)
    {
        ValidateCondition(request.Condition);
        var set = await LoadAsync(ruleSetId, tracking: true, ct);
        var rule = set.AddRule(request.Name, request.Condition, Mappers.ToDomain(request.Action), request.Description, request.Order);
        await db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task UpdateRuleAsync(Guid ruleSetId, Guid ruleId, UpsertRuleRequest request, CancellationToken ct)
    {
        ValidateCondition(request.Condition);
        var set = await LoadAsync(ruleSetId, tracking: true, ct);
        set.UpdateRule(ruleId, request.Name, request.Description, request.Condition, Mappers.ToDomain(request.Action), request.IsEnabled);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveRuleAsync(Guid ruleSetId, Guid ruleId, CancellationToken ct)
    {
        var set = await LoadAsync(ruleSetId, tracking: true, ct);
        set.RemoveRule(ruleId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Publishing retires the previously published version of the same family in the same transaction.</summary>
    public async Task<RuleSet> PublishAsync(Guid ruleSetId, PublishRuleSetRequest request, CancellationToken ct)
    {
        var set = await LoadAsync(ruleSetId, tracking: true, ct);
        foreach (var rule in set.Rules)
        {
            ValidateCondition(rule.Condition);
        }

        var previous = await db.RuleSets.Where(r => r.FamilyId == set.FamilyId && r.Status == RuleSetStatus.Published && r.Id != set.Id).ToListAsync(ct);
        foreach (var p in previous)
        {
            p.Retire();
        }

        set.Publish(tenant.UserId, request.ChangeNote, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return set;
    }

    public async Task<RuleSet> CreateNextVersionAsync(Guid ruleSetId, CancellationToken ct)
    {
        var set = await LoadAsync(ruleSetId, tracking: false, ct);
        if (await db.RuleSets.AnyAsync(r => r.FamilyId == set.FamilyId && r.Status == RuleSetStatus.Draft, ct))
        {
            throw ValidationException.For("ruleSetId", "This rule set already has a draft version.");
        }

        var next = set.CreateNextVersion(clock.GetUtcNow());
        db.RuleSets.Add(next);
        await db.SaveChangesAsync(ct);
        return next;
    }

    public static ValidateRuleResponse Validate(string condition)
    {
        if (RuleExpression.TryCompile(condition, out var expression, out var error))
        {
            return new ValidateRuleResponse(true, null, null, expression!.ReferencedFacts.ToList());
        }

        return new ValidateRuleResponse(false, error!.Detail, error.Position, []);
    }

    /// <summary>Evaluates a rule set (any status) against a real discrepancy without changing anything: "what would happen if I published this?"</summary>
    public async Task<DryRunResponse> DryRunAsync(Guid ruleSetId, Guid discrepancyId, CancellationToken ct)
    {
        var set = await LoadAsync(ruleSetId, tracking: false, ct);
        var d = await db.Discrepancies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == discrepancyId, ct) ?? throw new NotFoundException("Discrepancy", discrepancyId);
        var left = await db.SourceSystems.AsNoTracking().FirstAsync(s => s.Id == d.LeftSourceSystemId, ct);
        var right = await db.SourceSystems.AsNoTracking().FirstAsync(s => s.Id == d.RightSourceSystemId, ct);
        var ids = new[] { d.LeftRecordId, d.RightRecordId }.Where(x => x is not null).Select(x => x!.Value).ToList();
        var records = await db.SourceRecords.AsNoTracking().Where(r => ids.Contains(r.Id))
            .Select(r => new KeyedRecord(r.Id, r.MatchKey, r.ExternalId, r.PayloadJson, r.SourceUpdatedAt)).ToListAsync(ct);

        var ctx = new DiscrepancyContext(d, left, right, records.FirstOrDefault(r => r.Id == d.LeftRecordId), records.FirstOrDefault(r => r.Id == d.RightRecordId));
        var evaluation = engine.Evaluate(CompiledRuleSet.From(set), RuleFacts.Build(ctx, clock.GetUtcNow()));
        return new DryRunResponse(
            discrepancyId,
            evaluation.Matched?.Id,
            evaluation.Matched?.Name,
            Mappers.ToDto(evaluation.Action),
            evaluation.Trace.Select(t => new RuleEvaluationTraceDto(t.RuleId, t.RuleName, t.Matched, t.Error)).ToList());
    }

    private static void ValidateCondition(string condition)
    {
        if (!RuleExpression.TryCompile(condition, out _, out var error))
        {
            throw ValidationException.For("condition", $"{error!.Detail} (position {error.Position})");
        }
    }

    private async Task<RuleSet> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.RuleSets : db.RuleSets.AsNoTracking();
        return await query.Include(r => r.Rules).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("RuleSet", id);
    }
}
