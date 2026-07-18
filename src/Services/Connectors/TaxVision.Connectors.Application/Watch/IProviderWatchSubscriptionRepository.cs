using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Watch;

namespace TaxVision.Connectors.Application.Watch;

public interface IProviderWatchSubscriptionRepository
{
    Task AddAsync(ProviderWatchSubscription subscription, CancellationToken ct = default);

    Task<Result<ProviderWatchSubscription>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    Task<Result<ProviderWatchSubscription>> GetByIdAsync(Guid subscriptionId, CancellationToken ct = default);

    /// <summary>Usada por el webhook de Graph (Fase 7): la notificación solo trae el subscriptionId, nunca el AccountId.</summary>
    Task<Result<ProviderWatchSubscription>> GetBySubscriptionRefAsync(
        string subscriptionRef,
        CancellationToken ct = default
    );

    /// <summary>
    /// Sin filtro de tenant — mismo patrón que ITenantEmailAccountRepository.GetByIdAsync (background
    /// job). Excluye Status Failed/Removed: esas ya agotaron reintentos o fueron desconectadas, un
    /// reintento automático más es inútil hasta un reauth manual (ver WatchRenewalJob).
    /// </summary>
    Task<IReadOnlyList<ProviderWatchSubscription>> ListExpiringBeforeAsync(
        DateTime thresholdUtc,
        CancellationToken ct = default
    );
}
