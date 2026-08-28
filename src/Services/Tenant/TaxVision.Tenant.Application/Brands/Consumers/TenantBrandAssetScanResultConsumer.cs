using System.Linq;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Brands.Abstractions;
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
/// <para>Solo el logo de la superficie CRM alimenta el email: al confirmarlo se publica
/// <see cref="TenantLogoUpdatedIntegrationEvent"/> (MISMO contrato que el modelo viejo) para que
/// Scribe actualice su TenantLogoRef. El logo del portal y el favicon no viajan a Scribe.</para>
/// </summary>
public static class TenantBrandAssetScanResultConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent msg,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
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
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Solo el logo del CRM alimenta el email (Scribe). Portal/favicon no.
        if (brand.Surface == BrandSurface.Crm && key == BrandAssetKey.Logo)
        {
            await bus.PublishAsync(
                new TenantLogoUpdatedIntegrationEvent
                {
                    TenantId = msg.TenantId,
                    CloudStorageFileId = msg.FileId,
                    ContentType = msg.ContentType,
                    SizeBytes = msg.SizeBytes,
                    Width = width,
                    Height = height,
                    UpdatedAtUtc = updatedAtUtc,
                    CorrelationId = correlation.CorrelationId,
                }
            );
        }

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
            repo,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        FileBlockedByPolicyIntegrationEvent msg,
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
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
            repo,
            unitOfWork,
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
        ITenantBrandRepository repo,
        IUnitOfWork unitOfWork,
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

        logger.LogWarning(
            "Tenant {TenantId} brand asset {Key} (file {FileId}) {Reason}; discarding.",
            tenantId,
            asset.Key,
            fileId,
            reason
        );
        brand.DiscardPendingAsset(asset.Key, fileId);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
