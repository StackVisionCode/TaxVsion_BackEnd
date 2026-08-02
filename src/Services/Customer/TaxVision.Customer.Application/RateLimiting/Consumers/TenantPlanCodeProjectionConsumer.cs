using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.RateLimiting.Abstractions;
using TaxVision.Customer.Domain.RateLimiting;

namespace TaxVision.Customer.Application.RateLimiting.Consumers;

/// <summary>
/// RateLimit Fase 6 (piloto Customer) / Fase 1 (extracción BuildingBlocks) — wrapper de 1 línea
/// que delega en <see cref="TenantPlanCodeProjectionHandler"/> (BuildingBlocks.RateLimiting) la
/// lógica compartida de upsert idempotente + invalidación de caché; solo aporta la factory de
/// <see cref="TenantPlanCodeProjection"/>, que es lo único específico de Customer.
/// </summary>
public static class TenantPlanCodeProjectionConsumer
{
    public static Task Handle(
        TenantEntitlementsChangedIntegrationEvent evt,
        ITenantPlanCodeProjectionRepository repository,
        ITenantPlanCodeCacheInvalidator planCodeCache,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantPlanCodeProjection> logger,
        CancellationToken ct
    ) =>
        TenantPlanCodeProjectionHandler.Handle(
            evt,
            repository,
            planCodeCache,
            unitOfWork,
            correlation,
            logger,
            TenantPlanCodeProjection.Create,
            ct
        );
}
