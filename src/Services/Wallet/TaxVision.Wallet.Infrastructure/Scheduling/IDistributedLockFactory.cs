namespace TaxVision.Wallet.Infrastructure.Scheduling;

/// <summary>
/// Exclusión mutua entre réplicas para los jobs periódicos. Devuelve null si otra réplica ya tiene el candado.
/// </summary>
public interface IDistributedLockFactory
{
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);
}
