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

/// <summary>
/// CloudStorage descartó un archivo que Documents pidió guardar: ese FileId nunca existirá ni llegará
/// FileAvailable. Contraparte de <see cref="DocumentFileAvailableConsumer"/>, misma correlación
/// cross-tenant por FileId con validación explícita del tenant.
///
/// - Archivo principal: la generación en Uploading con ese FileId pasa a Failed y se anuncia
///   DocumentGenerationFailed (como cualquier fallo terminal; Tasks lo usa para ofrecer "Retry"). No hay
///   doble anuncio: Completed sólo se publica tras FileAvailable, que para un archivo rechazado no llega.
///   Si tenía copia de firma, se suelta y se pide su borrado a CloudStorage (FilesDeletionRequested).
/// - Copia de firma (P-18): se suelta si la generación aún no cerró, para que Completed no anuncie un
///   archivo inexistente (mismo criterio que cuando la subida de la copia falla en el momento).
/// </summary>
public static class DocumentFileSaveRejectedConsumer
{
    public const string ErrorCodeStorageRejected = "Documents.Storage.Rejected";
    public const string CopyDeletionReason = "generation_rejected";

    public static async Task Handle(
        FileSaveRejectedIntegrationEvent evt,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<FileSaveRejectedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        // El fanout trae los rechazos de todos los servicios: sólo los pedidos por Documents.
        if (!string.Equals(evt.RequestingService, IDocumentStorageClient.RequestingService, StringComparison.Ordinal))
            return;

        using (correlation.Push(ResolveCorrelationId(evt)))
        {
            var generation = await repository.GetByFileIdAsync(evt.FileId, ct);
            if (generation is not null)
            {
                await FailGenerationAsync(evt, generation, unitOfWork, bus, correlation, clock, logger, ct);
                return;
            }

            var withCopy = await repository.GetBySignatureCopyFileIdAsync(evt.FileId, ct);
            if (withCopy is not null)
            {
                await DetachSignatureCopyAsync(evt, withCopy, unitOfWork, clock, logger, ct);
                return;
            }

            logger.LogWarning(
                "FileSaveRejected {FileId} ({Reason}) matches no generation; ignoring.",
                evt.FileId,
                evt.Reason
            );
        }
    }

    private static async Task FailGenerationAsync(
        FileSaveRejectedIntegrationEvent evt,
        DocumentGeneration generation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (!TenantMatches(evt, generation, logger))
            return;

        if (generation.Status is DocumentGenerationStatus.Failed)
            return; // Redelivery: ya se soltó.

        var now = clock.GetUtcNow().UtcDateTime;
        var rejected = generation.RejectUpload(
            evt.FileId,
            ErrorCodeStorageRejected,
            $"The generated file was rejected by storage ({evt.Reason}).",
            now
        );
        if (rejected.IsFailure)
        {
            logger.LogWarning(
                "FileSaveRejected {FileId}: generation {GenerationId} left as {Status}: {Error}.",
                evt.FileId,
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
                ErrorCode = ErrorCodeStorageRejected,
            }
        );

        if (copyDeletion is not null)
            await bus.PublishAsync(copyDeletion);

        logger.LogWarning(
            "Generation {GenerationId} failed: CloudStorage rejected file {FileId} ({Reason}).",
            generation.Id,
            evt.FileId,
            evt.Reason
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
        FileSaveRejectedIntegrationEvent evt,
        DocumentGeneration generation,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (!TenantMatches(evt, generation, logger))
            return;

        var detached = generation.DetachSignatureCopy(evt.FileId, clock.GetUtcNow().UtcDateTime);
        if (detached.IsFailure)
        {
            // Ya cerrada: Completed anunció la copia y no hay contrato para retirarla.
            logger.LogWarning(
                "FileSaveRejected {FileId} ({Reason}): signature copy of generation {GenerationId} kept ({Status}): {Error}.",
                evt.FileId,
                evt.Reason,
                generation.Id,
                generation.Status,
                detached.Error.Message
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogWarning(
            "Generation {GenerationId}: signature copy {FileId} rejected by CloudStorage ({Reason}); it will not be announced.",
            generation.Id,
            evt.FileId,
            evt.Reason
        );
    }

    private static bool TenantMatches(
        FileSaveRejectedIntegrationEvent evt,
        DocumentGeneration generation,
        ILogger logger
    )
    {
        if (generation.TenantId == evt.TenantId)
            return true;

        logger.LogWarning(
            "FileSaveRejected {FileId} tenant {EventTenant} does not match generation {GenerationId} tenant {GenTenant}; ignoring.",
            evt.FileId,
            evt.TenantId,
            generation.Id,
            generation.TenantId
        );
        return false;
    }

    private static string ResolveCorrelationId(FileSaveRejectedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
