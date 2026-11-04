using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;

namespace TaxVision.Auth.Application.Onboarding.TermsVersions.Consumers;

/// <summary>
/// CloudStorage rechazó el documento legal que subió un PlatformAdmin: la <see cref="TermsVersion"/>
/// que lo referencia se retira (sin contenido y sin vigencia) para no exigir aceptar un texto que no
/// existe. Solo mira pedidos de Auth para documentos legales; un FileId desconocido o ya soltado es no-op.
/// </summary>
public static class TermsContentSaveRejectedConsumer
{
    public static async Task Handle(
        FileSaveRejectedIntegrationEvent msg,
        ITermsVersionRepository versions,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TermsVersion> logger,
        CancellationToken ct
    )
    {
        if (!TermsContentFile.IsTermsContent(msg.RequestingService, msg.TenantId, msg.OwnerType, msg.FolderType))
            return;

        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        var version = await versions.GetByContentFileIdAsync(msg.FileId, ct);
        if (version is null || !version.WithdrawRejectedContent(msg.FileId, DateTime.UtcNow))
        {
            logger.LogWarning(
                "CloudStorage rejected terms content file {FileId} ({Reason}) but no terms version references it; ignored.",
                msg.FileId,
                msg.Reason
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogWarning(
            "Terms version {TermsVersionId} ({Kind} {Version}, {Locale}) withdrawn: CloudStorage rejected its content file {FileId} ({Reason}). Publish it again.",
            version.Id,
            version.Kind,
            version.Version,
            version.Locale,
            msg.FileId,
            msg.Reason
        );
    }
}
