using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.FileStored;

/// <summary>Un archivo que Documents subió no va a existir (rechazo al guardar o al escanear).</summary>
internal sealed record StorageRejection(
    string Source,
    Guid FileId,
    Guid TenantId,
    string Reason,
    string ErrorCode,
    string ErrorMessage
);

/// <summary>
/// Lógica común de los rechazos de CloudStorage: correlación cross-tenant por FileId, validación del
/// tenant y suelta del archivo principal (generación Failed) o de la copia de firma.
/// </summary>
internal static class GenerationStorageRejection
{
    public const string CopyDeletionReason = "generation_rejected";

    /// <summary>Devuelve false si el FileId no es de ninguna generación (archivo de otro servicio).</summary>
    public static async Task<bool> ApplyAsync(
        StorageRejection rejection,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct
    )
    {
        var generation = await repository.GetByFileIdAsync(rejection.FileId, ct);
        if (generation is not null)
        {
            await FailGenerationAsync(rejection, generation, unitOfWork, bus, correlation, clock, logger, ct);
            return true;
        }

        var withCopy = await repository.GetBySignatureCopyFileIdAsync(rejection.FileId, ct);
        if (withCopy is not null)
        {
            await DetachSignatureCopyAsync(rejection, withCopy, unitOfWork, clock, logger, ct);
            return true;
        }

        return false;
    }

    private static async Task FailGenerationAsync(
        StorageRejection rejection,
        DocumentGeneration generation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (!TenantMatches(rejection, generation, logger))
            return;

        if (generation.Status is DocumentGenerationStatus.Failed)
            return; // Redelivery: ya se soltó.

        var now = clock.GetUtcNow().UtcDateTime;
        var rejected = generation.RejectUpload(rejection.FileId, rejection.ErrorCode, rejection.ErrorMessage, now);
        if (rejected.IsFailure)
        {
            logger.LogWarning(
                "{Source} {FileId}: generation {GenerationId} left as {Status}: {Error}.",
                rejection.Source,
                rejection.FileId,
                generation.Id,
                generation.Status,
                rejected.Error.Message
            );
            return;
        }

        var copyDeletion = ReleaseSignatureCopy(generation, now, correlation, logger);

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new DocumentGenerationFailedIntegrationEvent
            {
                TenantId = generation.TenantId,
                CorrelationId = correlation.CorrelationId,
                GenerationId = generation.Id,
                ExternalReferenceId = generation.ExternalReferenceId,
                DocumentType = generation.DocumentType.Value,
                OwnerType = generation.Owner.OwnerType,
                OwnerId = generation.Owner.OwnerId,
                ErrorCode = rejection.ErrorCode,
            }
        );

        if (copyDeletion is not null)
            await bus.PublishAsync(copyDeletion);

        logger.LogWarning(
            "Generation {GenerationId} failed: {Source} for file {FileId} ({Reason}).",
            generation.Id,
            rejection.Source,
            rejection.FileId,
            rejection.Reason
        );
    }

    // La copia de firma ya pudo quedar guardada: sin generación que la anuncie queda huérfana, así que
    // se suelta y se pide su borrado con el owner/carpeta con que se subió.
    private static FilesDeletionRequestedIntegrationEvent? ReleaseSignatureCopy(
        DocumentGeneration generation,
        DateTime now,
        ICorrelationContext correlation,
        ILogger logger
    )
    {
        if (generation.SignatureCopyFileId is not { } copyFileId)
            return null;

        var ownerType = generation.SignatureCopyOwnerType;
        var folderType = generation.SignatureCopyFolderType;
        if (generation.DetachSignatureCopy(copyFileId, now).IsFailure)
            return null;

        if (ownerType is null || folderType is null)
        {
            logger.LogWarning(
                "Generation {GenerationId}: signature copy {FileId} has no recorded owner/folder; deletion not requested.",
                generation.Id,
                copyFileId
            );
            return null;
        }

        return new FilesDeletionRequestedIntegrationEvent
        {
            TenantId = generation.TenantId,
            CorrelationId = correlation.CorrelationId,
            SourceService = IDocumentStorageClient.RequestingService,
            SourceReferenceId = generation.Id,
            Reason = CopyDeletionReason,
            Files =
            [
                new FileDeletionTarget
                {
                    FileId = copyFileId,
                    ExpectedOwnerType = ownerType,
                    ExpectedOwnerId = generation.Owner.OwnerId,
                    ExpectedFolderType = folderType,
                },
            ],
        };
    }

    private static async Task DetachSignatureCopyAsync(
        StorageRejection rejection,
        DocumentGeneration generation,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (!TenantMatches(rejection, generation, logger))
            return;

        var detached = generation.DetachSignatureCopy(rejection.FileId, clock.GetUtcNow().UtcDateTime);
        if (detached.IsFailure)
        {
            // Ya cerrada: Completed anunció la copia y no hay contrato para retirarla.
            logger.LogWarning(
                "{Source} {FileId} ({Reason}): signature copy of generation {GenerationId} kept ({Status}): {Error}.",
                rejection.Source,
                rejection.FileId,
                rejection.Reason,
                generation.Id,
                generation.Status,
                detached.Error.Message
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogWarning(
            "Generation {GenerationId}: signature copy {FileId} dropped by {Source} ({Reason}); it will not be announced.",
            generation.Id,
            rejection.FileId,
            rejection.Source,
            rejection.Reason
        );
    }

    private static bool TenantMatches(StorageRejection rejection, DocumentGeneration generation, ILogger logger)
    {
        if (generation.TenantId == rejection.TenantId)
            return true;

        logger.LogWarning(
            "{Source} {FileId} tenant {EventTenant} does not match generation {GenerationId} tenant {GenTenant}; ignoring.",
            rejection.Source,
            rejection.FileId,
            rejection.TenantId,
            generation.Id,
            generation.TenantId
        );
        return false;
    }
}
