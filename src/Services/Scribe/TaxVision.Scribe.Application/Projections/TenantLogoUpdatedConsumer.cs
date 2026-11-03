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
/// Upsert de TenantLogoRef por (tenant, superficie) ante un TenantLogoUpdatedIntegrationEvent.
/// Idempotente: un replay deja lo mismo y un evento más viejo se ignora. Invalida el L1 del tenant.
/// </summary>
public static class TenantLogoUpdatedConsumer
{
    public static async Task Handle(
        TenantLogoUpdatedIntegrationEvent evt,
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
                    "TenantLogoUpdated ignored for tenant {TenantId}: unknown surface '{Surface}'.",
                    evt.TenantId,
                    evt.Surface
                );
                return;
            }

            var existing = await repository.GetAsync(evt.TenantId, surface, ct);
            if (existing is null)
            {
                var logoRef = TenantLogoRef.Create(
                    evt.TenantId,
                    surface,
                    evt.CloudStorageFileId,
                    evt.ContentType,
                    evt.SizeBytes,
                    evt.Width,
                    evt.Height,
                    evt.UpdatedAtUtc
                );
                await repository.AddAsync(logoRef, ct);
            }
            else if (
                !existing.Update(
                    evt.CloudStorageFileId,
                    evt.ContentType,
                    evt.SizeBytes,
                    evt.Width,
                    evt.Height,
                    evt.UpdatedAtUtc
                )
            )
            {
                logger.LogInformation(
                    "TenantLogoUpdated ignored for tenant {TenantId} {Surface}: older than the projected logo.",
                    evt.TenantId,
                    surface
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            LogoCacheKeys.ForgetTenant(l1Cache, evt.TenantId);

            logger.LogInformation(
                "TenantLogoRef upserted for tenant {TenantId} surface {Surface}.",
                evt.TenantId,
                surface
            );
        }
    }

    private static string ResolveCorrelationId(TenantLogoUpdatedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
