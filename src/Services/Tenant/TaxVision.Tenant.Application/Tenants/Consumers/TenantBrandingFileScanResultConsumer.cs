using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.Consumers;

/// <summary>
/// Reacciona al resultado del escaneo asincrono (ClamAV + politica de contenido) del logo subido
/// directo a MinIO (ver ITenantBrandingCloudStorageClient.UploadAsync, patron Fase D1). A diferencia
/// de Customer/Signature, el FileId de estos eventos NO es la PK del propio aggregate — se
/// correlaciona por (evt.TenantId, Tenant.LogoFileId), que UploadTenantLogoHandler ya seteo de
/// forma optimista antes de publicar SaveFileRequestedIntegrationEvent. Estos 3 tipos de evento
/// fluyen por el mismo fanout "taxvision-events" que consumen otros servicios para SUS PROPIOS
/// archivos; un FileId que no coincide con el LogoFileId pendiente de este tenant simplemente no es
/// nuestro (o ya fue superado por un replace mas reciente) y se ignora.
/// </summary>
public static class TenantBrandingFileScanResultConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent msg,
        ITenantRepository repo,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TaxVision.Tenant.Domain.Tenant> logger,
        CancellationToken ct
    )
    {
        var tenant = await repo.GetByIdAsync(msg.TenantId, ct);
        if (tenant is null || tenant.LogoFileId != msg.FileId)
            return;

        var updatedAtUtc = DateTime.UtcNow;
        var setResult = tenant.ConfirmLogo(msg.FileId, msg.ContentType, msg.SizeBytes, null, null, updatedAtUtc);
        if (setResult.IsFailure)
        {
            logger.LogWarning(
                "TenantBranding: FileAvailable for tenant {TenantId} failed SetLogo invariant ({Error}); discarding.",
                msg.TenantId,
                setResult.Error.Message
            );
            tenant.RemoveLogo();
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new TenantLogoUpdatedIntegrationEvent
            {
                TenantId = msg.TenantId,
                CloudStorageFileId = msg.FileId,
                ContentType = msg.ContentType,
                SizeBytes = msg.SizeBytes,
                Width = null,
                Height = null,
                UpdatedAtUtc = updatedAtUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );

        logger.LogInformation("Tenant {TenantId} logo confirmed available (file {FileId}).", msg.TenantId, msg.FileId);
    }

    public static async Task Handle(
        FileInfectedDetectedIntegrationEvent msg,
        ITenantRepository repo,
        IUnitOfWork unitOfWork,
        ILogger<TaxVision.Tenant.Domain.Tenant> logger,
        CancellationToken ct
    )
    {
        var tenant = await repo.GetByIdAsync(msg.TenantId, ct);
        if (tenant is null || tenant.LogoFileId != msg.FileId)
            return;

        logger.LogWarning(
            "Tenant {TenantId} logo upload (file {FileId}) failed the security scan; discarding.",
            msg.TenantId,
            msg.FileId
        );
        tenant.DiscardPendingLogo(msg.FileId);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public static async Task Handle(
        FileBlockedByPolicyIntegrationEvent msg,
        ITenantRepository repo,
        IUnitOfWork unitOfWork,
        ILogger<TaxVision.Tenant.Domain.Tenant> logger,
        CancellationToken ct
    )
    {
        var tenant = await repo.GetByIdAsync(msg.TenantId, ct);
        if (tenant is null || tenant.LogoFileId != msg.FileId)
            return;

        logger.LogWarning(
            "Tenant {TenantId} logo upload (file {FileId}) was blocked by content policy; discarding.",
            msg.TenantId,
            msg.FileId
        );
        tenant.DiscardPendingLogo(msg.FileId);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
