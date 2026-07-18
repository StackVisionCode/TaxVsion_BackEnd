namespace TaxVision.Connectors.Application.Abstractions;

/// <summary>Handle IDisposable de un lock adquirido. <see cref="IsAcquired"/> indica si fue posible tomarlo.</summary>
public interface ILockHandle : IAsyncDisposable
{
    bool IsAcquired { get; }
    string Key { get; }
}

/// <summary>
/// Lock distribuido para coordinar workers en clúster (ej: refresh de un mismo OAuthToken desde 2
/// nodos a la vez). Implementación por defecto: Redis SET NX PX. Sin Redis configurado, degrada a
/// un no-op (single-node dev).
/// </summary>
public interface IDistributedLock
{
    Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
