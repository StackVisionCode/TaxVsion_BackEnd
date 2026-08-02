using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.RateLimiting.Abstractions;
using TaxVision.Customer.Domain.RateLimiting;

namespace TaxVision.Customer.Application.RateLimiting.Consumers;

/// <summary>
/// RateLimit Fase 6 (piloto Customer) — mantiene <see cref="TenantPlanCodeProjection"/>, la
/// proyección local que <c>EfTenantPlanCodeReader</c> (Infrastructure) expone como
/// <c>ITenantPlanCodeReader</c> al <c>RateLimitQuotaResolver</c>. Invalida el decorador de caché
/// vía <see cref="ITenantPlanCodeCacheInvalidator"/> al vuelo en vez de esperar el TTL de 5 min,
/// para que un cambio de plan aplique la nueva cuota casi de inmediato.
/// </summary>
public static class TenantPlanCodeProjectionConsumer
{
    public static async Task Handle(
        TenantEntitlementsChangedIntegrationEvent evt,
        ITenantPlanCodeProjectionRepository repository,
        ITenantPlanCodeCacheInvalidator planCodeCache,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantPlanCodeProjection> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var existing = await repository.GetAsync(evt.TenantId, ct);
            if (existing is null)
            {
                var projection = TenantPlanCodeProjection.Create(evt.TenantId, evt.PlanCode, evt.RevisionNumber);
                await repository.AddAsync(projection, ct);
            }
            else
            {
                existing.ApplyIfNewer(evt.PlanCode, evt.RevisionNumber);
            }

            await unitOfWork.SaveChangesAsync(ct);
            await planCodeCache.InvalidateAsync(evt.TenantId, ct);

            logger.LogInformation(
                "TenantPlanCodeProjection updated for tenant {TenantId}: plan {PlanCode} (revision {Revision}).",
                evt.TenantId,
                evt.PlanCode,
                evt.RevisionNumber
            );
        }
    }
}
