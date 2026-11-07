using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Projections;

/// <summary>
/// Soft-delete del TenantLogoRef de UNA superficie ante un TenantLogoRemovedIntegrationEvent; la otra
/// superficie queda intacta. Un retiro más viejo que el logo proyectado se ignora. Invalida el L1.
/// </summary>
public static class TenantLogoRemovedConsumer
{
    public static async Task Handle(
        TenantLogoRemovedIntegrationEvent evt,
        ITenantLogoRefRepository repository,
        IUnitOfWork unitOfWork,
        IMemoryCache l1Cache,
        ICorrelationContext correlation,
        ILogger<TenantLogoRef> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            if (!TenantLogoSurfaces.TryParse(evt.Surface, out var surface))
            {
                logger.LogWarning(
                    "TenantLogoRemoved ignored for tenant {TenantId}: unknown surface '{Surface}'.",
                    evt.TenantId,
                    evt.Surface
                );
                return;
            }

            var existing = await repository.GetAsync(evt.TenantId, surface, ct);
            if (existing is null)
            {
                await repository.AddAsync(TenantLogoRef.Tombstone(evt.TenantId, surface, evt.RemovedAtUtc), ct);
            }
            else if (!existing.MarkRemoved(evt.RemovedAtUtc))
            {
                logger.LogInformation(
                    "TenantLogoRemoved skipped for tenant {TenantId} {Surface}: a newer logo is projected.",
                    evt.TenantId,
                    surface
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            LogoCacheKeys.ForgetTenant(l1Cache, evt.TenantId);

            logger.LogInformation(
                "TenantLogoRef soft-deleted for tenant {TenantId} surface {Surface}.",
                evt.TenantId,
                surface
            );
        }
    }

    private static string ResolveCorrelationId(TenantLogoRemovedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
