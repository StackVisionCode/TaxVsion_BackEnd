using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Application.Projections;

/// <summary>
/// Mantiene la proyección <see cref="TenantLogoRef"/> por (tenant, superficie): Tenant publica
/// <c>TenantLogoUpdatedIntegrationEvent</c> al confirmarse el logo del CRM o del Portal, y
/// <c>TenantLogoRemovedIntegrationEvent</c> al borrarlo. Upsert idempotente; un evento más viejo que lo
/// proyectado se ignora y el retiro de una superficie no toca la otra. Los PDF que ve el cliente usan el
/// del Portal con el del CRM de respaldo (ver ITenantLogoResolver).
/// </summary>
public static class TenantLogoProjectionConsumer
{
    public static async Task Handle(
        TenantLogoUpdatedIntegrationEvent evt,
        ITenantLogoRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantLogoRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            if (!TryParseSurface(evt.Surface, evt.TenantId, logger, out var surface))
                return;

            var existing = await repository.GetAsync(evt.TenantId, surface, ct);
            if (existing is null)
            {
                var logoRef = TenantLogoRef.Create(evt.TenantId, surface, evt.UpdatedAtUtc);
                logoRef.SetLogo(evt.CloudStorageFileId, evt.ContentType, evt.UpdatedAtUtc);
                await repository.AddAsync(logoRef, ct);
            }
            else if (!existing.SetLogo(evt.CloudStorageFileId, evt.ContentType, evt.UpdatedAtUtc))
            {
                logger.LogInformation(
                    "Documents TenantLogoUpdated ignored for tenant {TenantId} {Surface}: older than the projected logo.",
                    evt.TenantId,
                    surface
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Documents TenantLogoRef updated for tenant {TenantId} surface {Surface}.",
                evt.TenantId,
                surface
            );
        }
    }

    public static async Task Handle(
        TenantLogoRemovedIntegrationEvent evt,
        ITenantLogoRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantLogoRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            if (!TryParseSurface(evt.Surface, evt.TenantId, logger, out var surface))
                return;

            var existing = await repository.GetAsync(evt.TenantId, surface, ct);
            if (existing is null || !existing.ClearLogo(evt.RemovedAtUtc))
                return;

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Documents TenantLogoRef cleared for tenant {TenantId} surface {Surface}.",
                evt.TenantId,
                surface
            );
        }
    }

    private static bool TryParseSurface(string? value, Guid tenantId, ILogger logger, out TenantLogoSurface surface)
    {
        if (TenantLogoSurfaces.TryParse(value, out surface))
            return true;

        logger.LogWarning(
            "Documents tenant logo event ignored for {TenantId}: unknown surface '{Surface}'.",
            tenantId,
            value
        );
        return false;
    }

    private static string ResolveCorrelationId(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
