using System.Linq;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Brands;
using Wolverine;

namespace TaxVision.Tenant.Application.Brands.Consumers;

/// <summary>
/// CloudStorage descartó el SaveFileRequested de un asset de marca (archivo vacío, política, cuota,
/// registro): ese FileId nunca existirá. El upload ya lo dejó Pending en la marca, así que se suelta
/// con el mismo método del dominio que el escaneo infectado/bloqueado — solo si el asset sigue Pending
/// con ESE FileId (un upload más nuevo no se toca).
///
/// <para>El upload pisó en la misma fila el logo anterior, así que tras soltarlo Tenant queda sin logo
/// en esa superficie. Si era el logo del CRM se publica TenantLogoRemoved (mismo camino que el delete)
/// para que Scribe/Documents no sigan mostrando un logo que Tenant ya no tiene.</para>
/// </summary>
public static class TenantBrandAssetSaveRejectedConsumer
{
    public static async Task Handle(
        FileSaveRejectedIntegrationEvent msg,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantBrand> logger,
        CancellationToken ct
    )
    {
        // El evento va al fanout: los rechazos de otros servicios no son nuestros.
        if (
            !string.Equals(
                msg.RequestingService,
                TenantCloudStorageRequests.RequestingService,
                StringComparison.Ordinal
            )
        )
            return;

        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        var brand = await repo.GetByAssetFileIdAsync(msg.TenantId, msg.FileId, ct);
        var asset = brand?.Assets.FirstOrDefault(a => a.FileId == msg.FileId);
        if (brand is null || asset is null)
        {
            logger.LogWarning(
                "Tenant {TenantId}: CloudStorage rejected file {FileId} ({Reason}) but no brand asset points to it; ignoring.",
                msg.TenantId,
                msg.FileId,
                msg.Reason
            );
            return;
        }

        var key = asset.Key;
        if (!brand.DiscardPendingAsset(key, msg.FileId))
        {
            logger.LogWarning(
                "Tenant {TenantId} brand asset {Key} (file {FileId}) rejected by CloudStorage ({Reason}) but it is no longer pending; ignoring.",
                msg.TenantId,
                key,
                msg.FileId,
                msg.Reason
            );
            return;
        }

        logger.LogWarning(
            "Tenant {TenantId} brand asset {Key} (file {FileId}) rejected by CloudStorage ({Reason}); discarded.",
            msg.TenantId,
            key,
            msg.FileId,
            msg.Reason
        );

        await unitOfWork.SaveChangesAsync(ct);
        await BrandCommandSupport.InvalidateAsync(cache, msg.TenantId, brand.Surface, ct);
        await BrandLogoEvents.PublishRemovedIfCrmLogoAsync(bus, correlation, msg.TenantId, brand.Surface, key);
    }
}
