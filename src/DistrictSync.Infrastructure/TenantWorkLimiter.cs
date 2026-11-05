using System.Collections.Concurrent;
using DistrictSync.Application.Abstractions;
using DistrictSync.Domain.Common;

namespace DistrictSync.Infrastructure;

/// <summary>
/// Per-tenant, per-kind semaphore. A tenant that queues a thousand ingestion batches only ever occupies
/// its quota of worker slots; the rest wait while other tenants' work proceeds.
/// </summary>
public sealed class TenantWorkLimiter : ITenantWorkLimiter
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(TenantId tenantId, string workKind, int limit, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd($"{tenantId}:{workKind}", _ => new SemaphoreSlim(Math.Max(1, limit), Math.Max(1, limit)));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    public int InFlight(TenantId tenantId, string workKind, int limit) =>
        _gates.TryGetValue($"{tenantId}:{workKind}", out var gate) ? Math.Max(1, limit) - gate.CurrentCount : 0;

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
