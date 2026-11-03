namespace TaxVision.Tenant.Infrastructure.Scheduling;

/// <summary>
/// Exclusión mutua entre réplicas para los jobs de Tenant (mismo contrato que Subscription y PaymentApp).
/// Devuelve null si otra réplica ya tiene el lock.
/// </summary>
public interface IDistributedLockFactory
{
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
