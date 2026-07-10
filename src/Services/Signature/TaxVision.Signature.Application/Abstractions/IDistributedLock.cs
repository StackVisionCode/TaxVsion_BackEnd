namespace TaxVision.Signature.Application.Abstractions;

/// <summary>Handle IDisposable de un lock adquirido. <see cref="IsAcquired"/> indica si fue posible tomarlo.</summary>
public interface ILockHandle : IAsyncDisposable
{
    bool IsAcquired { get; }
    string Key { get; }
}

/// <summary>
/// Lock distribuido para coordinar background workers en clúster. Implementación por
/// defecto: Redis SET NX PX. Cuando Redis no está configurado, degrada a un no-op
/// (single-node dev). Los workers deben tolerar "no adquirido" como "otro nodo se
/// encargó" y salir limpiamente.
/// </summary>
public interface IDistributedLock
{
    Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
