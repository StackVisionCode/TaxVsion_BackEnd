using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Locking;

/// <summary>
/// No-op fallback cuando Redis no está configurado. Single-node dev: siempre "adquirido".
/// En producción se registra <c>RedisDistributedLock</c>.
/// </summary>
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
