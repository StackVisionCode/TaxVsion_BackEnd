using TaxVision.Connectors.Application.Abstractions;

namespace TaxVision.Connectors.Infrastructure.Locking;

/// <summary>No-op fallback cuando Redis no está configurado. Single-node dev: siempre "adquirido".</summary>
public sealed class NoOpDistributedLock : IDistributedLock
{
    public Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<ILockHandle>(new AlwaysAcquired(key));

    private sealed class AlwaysAcquired(string key) : ILockHandle
    {
        public bool IsAcquired => true;
        public string Key { get; } = key;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
