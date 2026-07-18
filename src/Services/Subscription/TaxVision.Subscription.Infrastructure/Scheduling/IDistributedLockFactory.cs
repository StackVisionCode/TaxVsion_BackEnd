namespace TaxVision.Subscription.Infrastructure.Scheduling;

/// <summary>
/// Exclusión mutua entre réplicas del servicio para los jobs de renovación/expiración —
/// evita que dos instancias procesen el mismo batch a la vez. Devuelve null si no se pudo
/// adquirir el lock (otra réplica ya lo tiene).
/// </summary>
public interface IDistributedLockFactory
{
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
