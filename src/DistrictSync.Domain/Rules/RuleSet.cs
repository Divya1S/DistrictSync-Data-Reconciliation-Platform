using DistrictSync.Domain.Common;

namespace DistrictSync.Domain.Rules;

public enum RuleSetStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

/// <summary>A single condition → action pair. Conditions are written in the DistrictSync rule expression language.</summary>
public sealed class Rule : Entity
{
    private Rule() { }

    public Guid RuleSetId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int Order { get; private set; }
    public string Condition { get; private set; } = string.Empty;
    public RuleAction Action { get; private set; } = RuleAction.Ignore();
    public bool IsEnabled { get; private set; } = true;

    internal static Rule Create(Guid ruleSetId, string name, string? description, int order, string condition, RuleAction action) => new()
    {
        RuleSetId = ruleSetId,
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name), 200),
        Description = description is null ? null : Guard.MaxLength(description, 1000),
        Order = order,
        Condition = Guard.MaxLength(Guard.NotNullOrWhiteSpace(condition), 4000),
        Action = action,
    };

    internal Rule CloneFor(Guid ruleSetId) => new()
    {
        RuleSetId = ruleSetId,
        Name = Name,
        Description = Description,
        Order = Order,
        Condition = Condition,
        Action = Action,
        IsEnabled = IsEnabled,
    };

    internal void Update(string name, string? description, string condition, RuleAction action, bool isEnabled)
    {
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name), 200);
        Description = description is null ? null : Guard.MaxLength(description, 1000);
        Condition = Guard.MaxLength(Guard.NotNullOrWhiteSpace(condition), 4000);
        Action = action;
        IsEnabled = isEnabled;
    }

    internal void Reorder(int order) => Order = order;
}

/// <summary>
/// A versioned, ordered list of rules for one entity type. Rule sets are immutable once published: a
/// change is made by creating the next version as a draft, editing it, and publishing it, which retires
/// the previous version. Every discrepancy records the rule-set version that judged it, so decisions
/// remain explainable after the rules change.
/// </summary>
public sealed class RuleSet : AggregateRoot, ITenantScoped
{
    private readonly List<Rule> _rules = [];

    private RuleSet() { }

    public TenantId TenantId { get; private set; }

    /// <summary>Groups all versions of the same logical rule set.</summary>
    public Guid FamilyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Entity type this set applies to, or "*" for all.</summary>
    public string EntityType { get; private set; } = "*";

    public int Version { get; private set; }
    public RuleSetStatus Status { get; private set; }
    public IReadOnlyList<Rule> Rules => _rules.OrderBy(r => r.Order).ToList().AsReadOnly();
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public string? PublishedBy { get; private set; }
    public string? ChangeNote { get; private set; }

    public static RuleSet CreateDraft(TenantId tenantId, string name, string entityType, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        FamilyId = Guid.CreateVersion7(),
        Name = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name), 200),
        EntityType = string.IsNullOrWhiteSpace(entityType) ? "*" : Guard.MaxLength(entityType.Trim(), 64),
        Version = 1,
        Status = RuleSetStatus.Draft,
        CreatedAt = now,
    };

    public Rule AddRule(string name, string condition, RuleAction action, string? description = null, int? order = null)
    {
        EnsureDraft(nameof(AddRule));
        var rule = Rule.Create(Id, name, description, order ?? (_rules.Count == 0 ? 10 : _rules.Max(r => r.Order) + 10), condition, action);
        _rules.Add(rule);
        return rule;
    }

    public void UpdateRule(Guid ruleId, string name, string? description, string condition, RuleAction action, bool isEnabled)
    {
        EnsureDraft(nameof(UpdateRule));
        FindRule(ruleId).Update(name, description, condition, action, isEnabled);
    }

    public void RemoveRule(Guid ruleId)
    {
        EnsureDraft(nameof(RemoveRule));
        _rules.Remove(FindRule(ruleId));
    }

    public void Reorder(IReadOnlyList<Guid> orderedRuleIds)
    {
        EnsureDraft(nameof(Reorder));
        for (var i = 0; i < orderedRuleIds.Count; i++)
        {
            FindRule(orderedRuleIds[i]).Reorder((i + 1) * 10);
        }
    }

    public void Publish(string publishedBy, string? changeNote, DateTimeOffset now)
    {
        EnsureDraft(nameof(Publish));
        if (_rules.Count == 0)
        {
            throw new DomainException("empty_ruleset", "Cannot publish a rule set without rules.");
        }

        Status = RuleSetStatus.Published;
        PublishedAt = now;
        PublishedBy = Guard.MaxLength(publishedBy, 200);
        ChangeNote = changeNote is null ? null : Guard.MaxLength(changeNote, 1000);
        Raise(new RuleSetPublished(TenantId, Id, FamilyId, EntityType, Version, now));
    }

    public void Retire()
    {
        if (Status != RuleSetStatus.Published)
        {
            throw new InvalidStateTransitionException(nameof(RuleSet), Status.ToString(), nameof(Retire));
        }

        Status = RuleSetStatus.Retired;
    }

    /// <summary>Creates the next version of this family as an editable draft, copying all rules.</summary>
    public RuleSet CreateNextVersion(DateTimeOffset now)
    {
        if (Status == RuleSetStatus.Draft)
        {
            throw new InvalidStateTransitionException(nameof(RuleSet), Status.ToString(), nameof(CreateNextVersion));
        }

        var next = new RuleSet
        {
            TenantId = TenantId,
            FamilyId = FamilyId,
            Name = Name,
            EntityType = EntityType,
            Version = Version + 1,
            Status = RuleSetStatus.Draft,
            CreatedAt = now,
        };
        next._rules.AddRange(_rules.Select(r => r.CloneFor(next.Id)));
        return next;
    }

    private Rule FindRule(Guid ruleId) => _rules.FirstOrDefault(r => r.Id == ruleId) ?? throw new DomainException("rule_not_found", $"Rule {ruleId:D} is not part of this rule set.");

    private void EnsureDraft(string action)
    {
        if (Status != RuleSetStatus.Draft)
        {
            throw new InvalidStateTransitionException(nameof(RuleSet), Status.ToString(), action);
        }
    }
}

public sealed record RuleSetPublished(TenantId TenantId, Guid RuleSetId, Guid FamilyId, string EntityType, int Version, DateTimeOffset OccurredAt) : IDomainEvent;
