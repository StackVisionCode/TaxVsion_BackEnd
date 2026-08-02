using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using Microsoft.Extensions.Logging;
using TaxVision.Growth.Infrastructure.Persistence.RateLimiting.Abstractions;

namespace TaxVision.Growth.Infrastructure.Persistence.RateLimiting.Consumers;

// ---------------------------------------------------------------------------
// RateLimit Fase 2 — wrapper de 1 línea que delega en el handler compartido de BuildingBlocks.
// Mismo patrón que CloudStorage/Connectors y que PermissionsProjectionConsumers (RBAC Fase 7/8)
// en este mismo servicio.
//
// Vive en Growth.Infrastructure (no en un proyecto "Growth.Application", que no existe — Growth
// solo tiene Codes.Application y Referrals.Application, ninguno de los dos bounded contexts al
// que pertenezca este consumer transversal) — Program.cs ya agrega este assembly a la discovery
// de Wolverine (ver el comentario de PermissionsProjectionConsumers en Program.cs), así que este
// handler se registra automáticamente sin cambios adicionales.
// ---------------------------------------------------------------------------
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
