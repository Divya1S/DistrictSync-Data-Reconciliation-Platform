using System.Text.Json;
using DistrictSync.Application.Common;
using DistrictSync.Domain.Common;
using DistrictSync.Domain.Workflows;

namespace DistrictSync.Application.Workflows;

public sealed record WorkflowContext(WorkflowInstance Instance, TenantId TenantId, DateTimeOffset Now)
{
    public TState State<TState>() => JsonSerializer.Deserialize<TState>(Instance.StateJson, JsonDefaults.Options) ?? throw new InvalidOperationException($"Workflow {Instance.Id} has no state.");
}

/// <summary>What the definition wants the runner to do next.</summary>
public abstract record WorkflowStep
{
    public sealed record SleepUntil(DateTimeOffset At, object State) : WorkflowStep;

    public sealed record Complete(object State) : WorkflowStep;

    public static WorkflowStep Sleep(DateTimeOffset at, object state) => new SleepUntil(at, state);

    public static WorkflowStep Done(object state) => new Complete(state);
}

/// <summary>
/// A durable, resumable process. Definitions must be idempotent: the runner may execute the same step
/// twice if a worker dies between doing the work and persisting the result.
/// </summary>
public interface IWorkflowDefinition
{
    string Type { get; }

    Task<WorkflowStep> ExecuteAsync(WorkflowContext context, CancellationToken ct);
}

/// <summary>Thrown by a definition to signal a transient problem that should be retried with backoff.</summary>
public sealed class WorkflowRetryException(string message) : Exception(message);
