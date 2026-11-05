using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using DistrictSync.Application.Abstractions;
using DistrictSync.Application.Common;
using DistrictSync.Application.Tenancy;
using DistrictSync.Application.Workflows;
using DistrictSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistrictSync.Infrastructure.Workflows;

/// <summary>
/// Polls for due workflow instances, claims them with a lease, executes the matching definition and
/// persists the step result together with any domain events the step produced. A heartbeat extends the
/// lease while a step runs; if the process dies, the lease lapses and another runner picks the instance up.
/// </summary>
public sealed class WorkflowRunner(IServiceProvider services, IOptions<WorkflowOptions> options, TimeProvider clock, ILogger<WorkflowRunner> logger) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("DistrictSync.Workflows");
    private static readonly Meter Meter = new("DistrictSync.Workflows");
    private static readonly Counter<long> Steps = Meter.CreateCounter<long>("districtsync.workflows.steps");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("districtsync.workflows.failures");

    private readonly string _owner = Truncate($"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}", 100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Workflow runner {Owner} started", _owner);
        using var timer = new PeriodicTimer(options.Value.PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ran = await RunDueAsync(stoppingToken);
                if (ran > 0)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Workflow polling cycle failed; will retry");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> claimed;
        await using (var scope = services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetSystem("workflow-runner");
            claimed = await scope.ServiceProvider.GetRequiredService<IWorkflowStore>().ClaimDueAsync(_owner, options.Value.Lease, options.Value.BatchSize, ct);
        }

        if (claimed.Count == 0)
        {
            return 0;
        }

        await Parallel.ForEachAsync(claimed, new ParallelOptions { MaxDegreeOfParallelism = options.Value.Concurrency, CancellationToken = ct }, async (id, token) => await RunOneAsync(id, token));
        return claimed.Count;
    }

    private async Task RunOneAsync(Guid instanceId, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenant.SetSystem("workflow-runner");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowStore>();

        var instance = await db.WorkflowInstances.FirstOrDefaultAsync(w => w.Id == instanceId, ct);
        if (instance is null || instance.LockOwner != _owner)
        {
            return;
        }

        tenant.SetSystemForTenant(instance.TenantId, "workflow-runner");
        using var activity = ActivitySource.StartActivity($"workflow {instance.WorkflowType}");
        activity?.SetTag("districtsync.workflow.id", instanceId.ToString("D"));
        activity?.SetTag("districtsync.workflow.type", instance.WorkflowType);
        activity?.SetTag("districtsync.tenant.id", instance.TenantId.ToString());

        var definition = scope.ServiceProvider.GetServices<IWorkflowDefinition>().FirstOrDefault(d => d.Type == instance.WorkflowType);
        var now = clock.GetUtcNow();
        if (definition is null)
        {
            instance.RecordFailure($"No workflow definition registered for '{instance.WorkflowType}'.", now);
            await db.SaveChangesAsync(ct);
            return;
        }

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = HeartbeatAsync(store, instanceId, heartbeatCts.Token);
        try
        {
            var step = await definition.ExecuteAsync(new WorkflowContext(instance, instance.TenantId, now), ct);
            var after = clock.GetUtcNow();
            switch (step)
            {
                case WorkflowStep.SleepUntil sleep:
                    instance.Sleep(JsonSerializer.Serialize(sleep.State, JsonDefaults.Options), sleep.At, after);
                    break;
                case WorkflowStep.Complete done:
                    instance.Complete(JsonSerializer.Serialize(done.State, JsonDefaults.Options), after);
                    break;
            }

            Steps.Add(1, new KeyValuePair<string, object?>("type", instance.WorkflowType));
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Failures.Add(1, new KeyValuePair<string, object?>("type", instance.WorkflowType));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            db.ClearTracking();
            var fresh = await db.WorkflowInstances.FirstAsync(w => w.Id == instanceId, ct);
            var willRetry = fresh.RecordFailure(ex.Message, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            if (willRetry)
            {
                logger.LogWarning(ex, "Workflow {WorkflowId} ({Type}) attempt {Attempt} failed; retrying at {WakeUpAt}", instanceId, fresh.WorkflowType, fresh.Attempts, fresh.WakeUpAt);
            }
            else
            {
                logger.LogError(ex, "Workflow {WorkflowId} ({Type}) failed permanently after {Attempt} attempts", instanceId, fresh.WorkflowType, fresh.Attempts);
            }
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }
    }

    private async Task HeartbeatAsync(IWorkflowStore store, Guid instanceId, CancellationToken ct)
    {
        var interval = options.Value.Lease / 3;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);
            await store.ExtendLeaseAsync(instanceId, _owner, options.Value.Lease, ct);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
