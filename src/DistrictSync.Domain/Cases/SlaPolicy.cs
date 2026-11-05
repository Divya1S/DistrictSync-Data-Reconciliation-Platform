using DistrictSync.Domain.Rules;

namespace DistrictSync.Domain.Cases;

/// <summary>
/// Service-level targets by priority. When a case passes its due time without reaching a terminal state
/// the workflow engine escalates it and extends the due time by the escalation step, up to a maximum
/// number of escalations.
/// </summary>
public sealed record SlaPolicy(TimeSpan InitialDue, TimeSpan EscalationStep, int MaxEscalations)
{
    public static SlaPolicy For(CasePriority priority) => priority switch
    {
        CasePriority.Urgent => new SlaPolicy(TimeSpan.FromHours(4), TimeSpan.FromHours(2), 3),
        CasePriority.High => new SlaPolicy(TimeSpan.FromHours(24), TimeSpan.FromHours(8), 3),
        CasePriority.Normal => new SlaPolicy(TimeSpan.FromHours(72), TimeSpan.FromHours(24), 2),
        _ => new SlaPolicy(TimeSpan.FromDays(7), TimeSpan.FromDays(3), 1),
    };
}
