using System.Collections.Concurrent;
using TaxVision.Subscription.Infrastructure.Scheduling;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Candado compartido en memoria: varias instancias con el mismo fake simulan varias réplicas.</summary>
public sealed class FakeDistributedLockFactory : IDistributedLockFactory
{
    private readonly ConcurrentDictionary<string, byte> _held = new(StringComparer.Ordinal);

    public List<string> RequestedKeys { get; } = [];

    public bool IsHeld(string key) => _held.ContainsKey(key);

    public void Hold(string key) => _held[key] = 0;

    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        lock (RequestedKeys)
            RequestedKeys.Add(key);

        return Task.FromResult<IAsyncDisposable?>(_held.TryAdd(key, 0) ? new Lease(_held, key) : null);
    }

    private sealed class Lease(ConcurrentDictionary<string, byte> held, string key) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            held.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }
}
