using System.Collections.Concurrent;
using TaxVision.Subscription.Application.Catalog.Abstractions;

namespace TaxVision.Subscription.Infrastructure.Catalog.Concurrency;

public sealed class InMemoryModuleEditSerializer : IModuleEditSerializer
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IAsyncDisposable> AcquireAsync(string moduleCode, CancellationToken ct)
    {
        var normalizedCode = moduleCode.Trim().ToLowerInvariant();
        var gate = _locks.GetOrAdd(normalizedCode, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
