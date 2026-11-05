using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Rules;
using DistrictSync.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DistrictSync.Application.Workflows;

/// <summary>
/// Wakes at the case's due time. If the case is still open it escalates and sleeps until the new due time;
/// when the case closes (or hits its escalation ceiling) the workflow completes.
/// </summary>
public sealed class CaseSlaWorkflow(IAppDbContext db, ILogger<CaseSlaWorkflow> logger) : IWorkflowDefinition
{
    public string Type => WorkflowTypes.CaseSla;

    public async Task<WorkflowStep> ExecuteAsync(WorkflowContext context, CancellationToken ct)
    {
        var state = context.State<CaseSlaState>();
        var c = await db.Cases.Include(x => x.History).FirstOrDefaultAsync(x => x.Id == state.CaseId, ct);
        if (c is null)
        {
            logger.LogWarning("SLA workflow {WorkflowId}: case {CaseId} no longer exists", context.Instance.Id, state.CaseId);
            return WorkflowStep.Done(state);
        }

        if (c.IsTerminal || c.State == CaseState.Approved)
        {
            return WorkflowStep.Done(state with { EscalationsObserved = c.EscalationLevel });
        }

        if (c.DueAt > context.Now)
        {
            // Nudged early (the case changed); just re-arm the timer.
            return WorkflowStep.Sleep(c.DueAt, state);
        }

        var escalated = c.Escalate($"SLA breached: due {c.DueAt:u}, now {context.Now:u}.", context.Now);
        if (!escalated)
        {
            logger.LogWarning("Case {CaseId} is overdue and at maximum escalation level {Level}", c.Id, c.EscalationLevel);
            return WorkflowStep.Done(state with { EscalationsObserved = c.EscalationLevel });
        }

        return WorkflowStep.Sleep(c.DueAt, state with { EscalationsObserved = c.EscalationLevel });
    }
}
