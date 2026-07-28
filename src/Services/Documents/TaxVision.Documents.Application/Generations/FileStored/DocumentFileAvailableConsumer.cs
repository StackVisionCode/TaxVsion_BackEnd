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
/// CloudStorage confirmó que el archivo generado quedó almacenado (pasó el scan). Cierra la generación:
/// Uploading → Stored → Completed y publica DocumentGenerationCompleted (el consumidor —p.ej. Billing—
/// reacciona con el FileId, nunca con bytes).
///
/// Corre en un scope de Wolverine sin tenant ambiental y la búsqueda es CROSS-TENANT por FileId
/// (todos los servicios comparten el exchange taxvision-events, así que este consumer ve FileAvailable
/// de archivos que no son de Documents). Por eso: correlación por FileId (IgnoreQueryFilters) y luego
/// validación explícita del tenant contra la generación encontrada.
/// </summary>
public static class DocumentFileAvailableConsumer
{
    private const string DocumentTypeInvoice = "Invoice";

    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<FileAvailableIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt)))
        {
            var generation = await repository.GetByFileIdAsync(evt.FileId, ct);
            if (generation is null)
                return; // El archivo no corresponde a ninguna generación de Documents.

            if (generation.TenantId != evt.TenantId)
            {
                logger.LogWarning(
                    "FileAvailable {FileId} tenant {EventTenant} does not match generation {GenerationId} tenant {GenTenant}; ignoring.",
                    evt.FileId,
                    evt.TenantId,
                    generation.Id,
                    generation.TenantId
                );
                return;
            }

            if (generation.Status is DocumentGenerationStatus.Completed)
                return; // Redelivery: ya cerrada.

            var now = clock.GetUtcNow().UtcDateTime;
            var storage = new StorageReference(evt.FileId, evt.ContentType, evt.SizeBytes, evt.ChecksumSha256);

            var stored = generation.MarkStored(storage, now);
            if (stored.IsFailure)
            {
                logger.LogWarning(
                    "Generation {GenerationId} could not be marked Stored from {Status}: {Error}.",
                    generation.Id,
                    generation.Status,
                    stored.Error.Message
                );
                return;
            }

            var completed = generation.Complete(now);
            if (completed.IsFailure)
            {
                logger.LogWarning(
                    "Generation {GenerationId} could not be Completed: {Error}.",
                    generation.Id,
                    completed.Error.Message
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            await PublishClosedAsync(generation, storage, correlation.CorrelationId, bus);

            logger.LogInformation(
                "Generation {GenerationId} completed with stored file {FileId} ({Bytes} bytes).",
                generation.Id,
                evt.FileId,
                evt.SizeBytes
            );
        }
    }

    private static async Task PublishClosedAsync(
        DocumentGeneration generation,
        StorageReference storage,
        string correlationId,
        IMessageBus bus
    )
    {
        await bus.PublishAsync(
            new DocumentStoredIntegrationEvent
            {
                TenantId = generation.TenantId,
                CorrelationId = correlationId,
                GenerationId = generation.Id,
                FileId = storage.FileId,
                SizeBytes = storage.SizeBytes,
            }
        );

        await bus.PublishAsync(
            new DocumentGenerationCompletedIntegrationEvent
            {
                TenantId = generation.TenantId,
                CorrelationId = correlationId,
                GenerationId = generation.Id,
                DocumentType = DocumentTypeInvoice,
                OwnerType = generation.Owner.OwnerType,
                OwnerId = generation.Owner.OwnerId,
                DocumentVersion = generation.DocumentVersion,
                FileId = storage.FileId,
                FileName = generation.FileName ?? $"{generation.Id:N}.pdf",
                ContentType = storage.ContentType,
                SizeBytes = storage.SizeBytes,
                ContentHash = generation.ContentHash?.Value,
            }
        );
    }

    private static string ResolveCorrelationId(FileAvailableIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
