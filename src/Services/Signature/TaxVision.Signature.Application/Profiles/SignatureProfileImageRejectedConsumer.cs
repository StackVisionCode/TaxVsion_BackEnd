using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Application.Profiles;

/// <summary>
/// La imagen de una firma reutilizable nunca estará disponible: CloudStorage rechazó guardarla, salió
/// infectada, la bloqueó la moderación o el scan no la aceptó. La firma es esa imagen (FileId obligatorio,
/// sin flujo para reemplazarla), así que se quita igual que con Delete: deja de mostrarse rota, libera el
/// cupo de firmas activas y el usuario puede subirla de nuevo. Solo se toca la firma cuyo FileId es
/// exactamente el del evento. Las solicitudes que ya copiaron ese FileId caen al sello tipográfico al sellar.
/// </summary>
public static class SignatureProfileImageRejectedConsumer
{
    public static async Task Handle(
        FileSaveRejectedIntegrationEvent evt,
        ISignatureProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureProfile> logger,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.RequestingService, SignatureStorage.RequestingService, StringComparison.Ordinal))
            return;

        await RemoveAsync(evt, evt.FileId, evt.Reason, profiles, unitOfWork, correlation, logger, ct);
    }

    public static Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        ISignatureProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureProfile> logger,
        CancellationToken ct
    ) => RemoveAsync(evt, evt.FileId, "Infected", profiles, unitOfWork, correlation, logger, ct);

    public static Task Handle(
        FileBlockedByPolicyIntegrationEvent evt,
        ISignatureProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureProfile> logger,
        CancellationToken ct
    ) => RemoveAsync(evt, evt.FileId, evt.PolicyReason, profiles, unitOfWork, correlation, logger, ct);

    public static Task Handle(
        FileScanFailedIntegrationEvent evt,
        ISignatureProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureProfile> logger,
        CancellationToken ct
    ) => RemoveAsync(evt, evt.FileId, evt.Reason, profiles, unitOfWork, correlation, logger, ct);

    private static async Task RemoveAsync(
        BuildingBlocks.Messaging.IntegrationEvent evt,
        Guid fileId,
        string reason,
        ISignatureProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;
        using (correlation.Push(correlationId))
        {
            var profile = await profiles.GetByFileIdAsync(evt.TenantId, fileId, ct);
            if (profile is null)
                return; // no es la imagen de una firma reutilizable (o ya se quitó)

            profiles.Remove(profile);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogWarning(
                "Signature profile {ProfileId} of tenant {TenantId} was removed: its image {FileId} was not accepted by storage ({Reason}).",
                profile.Id,
                evt.TenantId,
                fileId,
                reason
            );
        }
    }
}
