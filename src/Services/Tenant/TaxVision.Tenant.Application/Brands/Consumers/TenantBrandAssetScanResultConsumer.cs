using System.Linq;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Application.Brands.Consumers;

/// <summary>
/// Confirma o descarta un asset de marca (modelo TenantBrands) según el resultado del escaneo
/// asíncrono de CloudStorage. Correlaciona por (TenantId, TenantBrandAsset.FileId) — el evento solo
/// trae TenantId+FileId, así que se busca la marca que contiene ese fileId y se lee su assetKey. Un
/// FileId que no es de ninguna marca de este tenant no es nuestro (o lo pertenece al modelo viejo,
/// que tiene su propio consumer) y se ignora, igual que el consumer del logo legado.
///
/// <para>Al confirmar el logo del CRM o del Portal se publica
/// <see cref="TenantLogoUpdatedIntegrationEvent"/> con su superficie: Scribe, Documents y Signature
/// guardan uno por superficie (lo que ve el cliente usa el del Portal). El favicon no viaja.</para>
/// </summary>
public static class TenantBrandAssetScanResultConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent msg,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantBrand> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(CorrelationOf(msg.CorrelationId, msg.EventId));

        var brand = await repo.GetByAssetFileIdAsync(msg.TenantId, msg.FileId, ct);
        var asset = brand?.Assets.FirstOrDefault(a => a.FileId == msg.FileId);
        if (brand is null || asset is null)
            return;

        // Width/height ya se midieron en el upload (bytes reales) y CloudStorage no transcodea.
        var width = asset.Width;
        var height = asset.Height;
        var key = asset.Key;

        var updatedAtUtc = DateTime.UtcNow;
        var setResult = brand.ConfirmAsset(
            key,
            msg.FileId,
            msg.ContentType,
            msg.SizeBytes,
            width,
            height,
            updatedAtUtc
        );
        if (setResult.IsFailure)
        {
            logger.LogWarning(
                "TenantBrand: FileAvailable for tenant {TenantId} asset {Key} failed invariant ({Error}); discarding.",
                msg.TenantId,
                key,
                setResult.Error.Message
            );
            brand.RemoveAsset(key);
            await unitOfWork.SaveChangesAsync(ct);
            await BrandCommandSupport.InvalidateAsync(cache, msg.TenantId, brand.Surface, ct);
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
        // El GET de la marca cachea 5 min: sin esto el CRM seguía viendo "Pending" tras confirmarse.
        await BrandCommandSupport.InvalidateAsync(cache, msg.TenantId, brand.Surface, ct);

        // El logo de CRM y Portal sale con su superficie; el favicon no.
        await BrandLogoEvents.PublishUpdatedIfLogoAsync(
            bus,
            correlation,
            msg.TenantId,
            brand.Surface,
            key,
            new LogoSnapshot(msg.FileId, msg.ContentType, msg.SizeBytes, width, height, updatedAtUtc)
        );

        logger.LogInformation(
            "Tenant {TenantId} brand asset confirmed: surface={Surface} key={Key} file={FileId}.",
            msg.TenantId,
            brand.Surface,
            key,
            msg.FileId
        );
    }

    public static Task Handle(
        FileInfectedDetectedIntegrationEvent msg,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantBrand> logger,
        CancellationToken ct
    ) =>
        DiscardAsync(
            msg.TenantId,
            msg.FileId,
            "failed the security scan",
            msg.CorrelationId,
            msg.EventId,
            pendingOnly: false,
            repo,
            unitOfWork,
            cache,
            bus,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        FileBlockedByPolicyIntegrationEvent msg,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantBrand> logger,
        CancellationToken ct
    ) =>
        DiscardAsync(
            msg.TenantId,
            msg.FileId,
            "was blocked by content policy",
            msg.CorrelationId,
            msg.EventId,
            pendingOnly: false,
            repo,
            unitOfWork,
            cache,
            bus,
            correlation,
            logger,
            ct
        );

    // CloudStorage no pudo escanear (blob_missing, scan_stalled): el upload nunca se confirmará.
    public static Task Handle(
        FileScanFailedIntegrationEvent msg,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantBrand> logger,
        CancellationToken ct
    ) =>
        DiscardAsync(
            msg.TenantId,
            msg.FileId,
            $"could not be scanned ({msg.Reason})",
            msg.CorrelationId,
            msg.EventId,
            pendingOnly: true,
            repo,
            unitOfWork,
            cache,
            bus,
            correlation,
            logger,
            ct
        );

    private static async Task DiscardAsync(
        Guid tenantId,
        Guid fileId,
        string reason,
        string? correlationId,
        Guid eventId,
        bool pendingOnly,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantBrand> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(CorrelationOf(correlationId, eventId));

        var brand = await repo.GetByAssetFileIdAsync(tenantId, fileId, ct);
        var asset = brand?.Assets.FirstOrDefault(a => a.FileId == fileId);
        if (brand is null || asset is null)
            return;

        // Rechazado por el escaneo: se quita aunque ya figure confirmado; un fallo de escaneo solo descarta
        // el pendiente. Replay o asset reemplazado: nada.
        var key = asset.Key;
        var removed = pendingOnly ? brand.DiscardPendingAsset(key, fileId) : brand.RevokeAsset(key, fileId);
        if (!removed)
            return;

        logger.LogWarning(
            "Tenant {TenantId} brand asset {Key} (file {FileId}) {Reason}; discarding.",
            tenantId,
            key,
            fileId,
            reason
        );
        await unitOfWork.SaveChangesAsync(ct);
        // Igual que el rechazo de CloudStorage: el upload pisó el logo anterior, así que Tenant queda sin
        // logo y Scribe/Documents deben enterarse.
        await BrandCommandSupport.InvalidateAsync(cache, tenantId, brand.Surface, ct);
        await BrandLogoEvents.PublishRemovedIfLogoAsync(bus, correlation, tenantId, brand.Surface, key);
    }

    private static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
