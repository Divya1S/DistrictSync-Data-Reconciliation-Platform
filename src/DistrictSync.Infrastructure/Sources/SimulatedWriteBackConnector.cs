using DistrictSync.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistrictSync.Infrastructure.Sources;

/// <summary>
/// Stand-in for real system connectors (Workday, SAP, AD...). Logs what would be written and optionally
/// injects transient failures so retry/escalation paths can be exercised end to end.
/// </summary>
public sealed class SimulatedWriteBackConnector(IOptions<WriteBackOptions> options, ILogger<SimulatedWriteBackConnector> logger) : IWriteBackConnector
{
    public async Task<WriteBackResult> ApplyAsync(WriteBackRequest request, CancellationToken ct)
    {
        await Task.Delay(options.Value.SimulatedLatency, ct);

        if (options.Value.TransientFailureRate > 0 && Random.Shared.NextDouble() < options.Value.TransientFailureRate)
        {
            logger.LogWarning("Simulated transient write-back failure for {IdempotencyKey} → {Target}", request.IdempotencyKey, request.TargetSourceSystemId);
            return new WriteBackResult(false, "Simulated upstream timeout.", IsTransient: true);
        }

        logger.LogInformation("Write-back {Action} to source {Target} for {EntityType} {MatchKey} [{IdempotencyKey}]: {Explanation} payload={Payload}",
            request.Action, request.TargetSourceSystemId, request.EntityType, request.MatchKey, request.IdempotencyKey, request.Explanation, request.PayloadJson);
        return new WriteBackResult(true, "Applied (simulated).", IsTransient: false);
    }
}
