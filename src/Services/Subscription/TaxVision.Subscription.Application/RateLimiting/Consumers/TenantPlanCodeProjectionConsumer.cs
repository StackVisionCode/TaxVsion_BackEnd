using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.RateLimiting.Abstractions;
using TaxVision.Subscription.Domain.RateLimiting;

namespace TaxVision.Subscription.Application.RateLimiting.Consumers;

/// <summary>
/// RateLimit Fase 2 — wrapper de 1 línea que delega en el handler compartido de BuildingBlocks.
/// Subscription consume su PROPIO evento (ver <see cref="TenantPlanCodeProjection"/> remarks): la
/// cola "subscription-events" ya está bindeada de forma fanout al exchange "taxvision-events" para
/// TenantCreated, así que este handler simplemente se suma al mismo listener vía discovery de
/// Wolverine — sin binding nuevo.
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
