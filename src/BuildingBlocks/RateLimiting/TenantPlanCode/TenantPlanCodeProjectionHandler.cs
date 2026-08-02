using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.RateLimiting;

/// <summary>
/// RateLimit Fase 1 — lógica compartida del consumer de <see cref="TenantEntitlementsChangedIntegrationEvent"/>
/// (upsert idempotente por revisión monotónica + invalidación de caché), extraída de la copia
/// original de Customer (Fase 6). Cada servicio registra un handler local de 1 línea que delega
/// acá, pasando su propia factory de proyección (<c>TProjection.Create</c>) — mismo criterio que
/// el resto de esta fase: la forma se comparte, la persistencia no.
/// </summary>
public static class TenantPlanCodeProjectionHandler
{
    public static async Task Handle<TProjection>(
        TenantEntitlementsChangedIntegrationEvent evt,
        ITenantPlanCodeProjectionRepository<TProjection> repository,
        ITenantPlanCodeCacheInvalidator planCodeCache,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger logger,
        Func<Guid, string, long, TProjection> create,
        CancellationToken ct
    )
        where TProjection : ITenantPlanCodeProjection
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
                var projection = create(evt.TenantId, evt.PlanCode, evt.RevisionNumber);
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
