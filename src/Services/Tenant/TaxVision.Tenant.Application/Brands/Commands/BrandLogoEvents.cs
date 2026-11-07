using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Application.Brands.Commands;

/// <summary>
/// Anuncia los cambios de logo de las superficies que salen del servicio (CRM y Portal) con
/// <see cref="TenantLogoUpdatedIntegrationEvent"/> / <see cref="TenantLogoRemovedIntegrationEvent"/>,
/// cada uno con su <c>Surface</c>. Scribe, Documents y Signature guardan un logo por superficie. El
/// favicon no viaja: nadie lo usa fuera de los navegadores del CRM y el portal.
/// </summary>
internal static class BrandLogoEvents
{
    /// <summary>Superficie del contrato para una superficie de Tenant; null = no sale del servicio.</summary>
    public static string? ContractSurfaceOf(BrandSurface surface) =>
        surface switch
        {
            BrandSurface.Crm => TenantBrandSurfaces.Crm,
            BrandSurface.Portal => TenantBrandSurfaces.Portal,
            _ => null,
        };

    public static async Task PublishUpdatedIfLogoAsync(
        IMessageBus bus,
        ICorrelationContext correlation,
        Guid tenantId,
        BrandSurface surface,
        BrandAssetKey key,
        LogoSnapshot logo
    )
    {
        if (key != BrandAssetKey.Logo || ContractSurfaceOf(surface) is not { } contractSurface)
            return;

        await bus.PublishAsync(
            new TenantLogoUpdatedIntegrationEvent
            {
                TenantId = tenantId,
                Surface = contractSurface,
                CloudStorageFileId = logo.FileId,
                ContentType = logo.ContentType,
                SizeBytes = logo.SizeBytes,
                Width = logo.Width,
                Height = logo.Height,
                UpdatedAtUtc = logo.ConfirmedAtUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }

    public static async Task PublishRemovedIfLogoAsync(
        IMessageBus bus,
        ICorrelationContext correlation,
        Guid tenantId,
        BrandSurface surface,
        BrandAssetKey key,
        DateTime removedAtUtc
    )
    {
        if (key != BrandAssetKey.Logo || ContractSurfaceOf(surface) is not { } contractSurface)
            return;

        await bus.PublishAsync(
            new TenantLogoRemovedIntegrationEvent
            {
                TenantId = tenantId,
                Surface = contractSurface,
                RemovedAtUtc = removedAtUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }
}

/// <summary>Datos de un logo confirmado tal como viajan en el evento.</summary>
internal sealed record LogoSnapshot(
    Guid FileId,
    string ContentType,
    long SizeBytes,
    int? Width,
    int? Height,
    DateTime ConfirmedAtUtc
);
