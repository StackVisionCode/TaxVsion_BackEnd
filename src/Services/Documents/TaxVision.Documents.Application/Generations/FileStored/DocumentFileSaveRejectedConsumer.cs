using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
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
    public const string CopyDeletionReason = GenerationStorageRejection.CopyDeletionReason;

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
            var rejection = new StorageRejection(
                "FileSaveRejected",
                evt.FileId,
                evt.TenantId,
                evt.Reason,
                ErrorCodeStorageRejected,
                $"The generated file was rejected by storage ({evt.Reason})."
            );

            var matched = await GenerationStorageRejection.ApplyAsync(
                rejection,
                repository,
                unitOfWork,
                bus,
                correlation,
                clock,
                logger,
                ct
            );
            if (!matched)
                logger.LogWarning(
                    "FileSaveRejected {FileId} ({Reason}) matches no generation; ignoring.",
                    evt.FileId,
                    evt.Reason
                );
        }
    }

    private static string ResolveCorrelationId(FileSaveRejectedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
