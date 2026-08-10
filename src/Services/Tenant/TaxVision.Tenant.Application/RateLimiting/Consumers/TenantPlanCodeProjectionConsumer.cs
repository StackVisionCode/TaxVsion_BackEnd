using BuildingBlocks.Common;
using BuildingBlocks.Messaging.RateLimiting;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.RateLimiting.Abstractions;
using TaxVision.Tenant.Domain.RateLimiting;

namespace TaxVision.Tenant.Application.RateLimiting.Consumers;

/// <summary>RateLimit Fase 2 — wrapper de 1 línea que delega en el handler compartido de BuildingBlocks.</summary>
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
