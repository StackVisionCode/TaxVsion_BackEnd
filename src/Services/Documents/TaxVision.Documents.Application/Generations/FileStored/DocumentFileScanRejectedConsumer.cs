using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using Wolverine;

namespace TaxVision.Documents.Application.Generations.FileStored;

/// <summary>
/// CloudStorage rechazó en el escaneo un archivo ya registrado (fallo definitivo, infectado o bloqueado
/// por política): no llegará FileAvailable. Misma suelta que <see cref="DocumentFileSaveRejectedConsumer"/>.
/// Estos eventos no traen RequestingService: si el FileId no es de ninguna generación, es de otro servicio.
/// Idempotente: RejectUpload sólo actúa desde Uploading con ese FileId.
/// </summary>
public static class DocumentFileScanRejectedConsumer
{
    public const string ErrorCodeScanRejected = "Documents.Storage.ScanRejected";

    public static Task Handle(
        FileScanFailedIntegrationEvent evt,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<FileScanFailedIntegrationEvent> logger,
        CancellationToken ct
    ) =>
        RejectAsync(
            evt,
            "FileScanFailed",
            evt.FileId,
            evt.Reason,
            repository,
            unitOfWork,
            bus,
            correlation,
            clock,
            logger,
            ct
        );

    public static Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<FileInfectedDetectedIntegrationEvent> logger,
        CancellationToken ct
    ) =>
        RejectAsync(
            evt,
            "FileInfectedDetected",
            evt.FileId,
            "infected",
            repository,
            unitOfWork,
            bus,
            correlation,
            clock,
            logger,
            ct
        );

    public static Task Handle(
        FileBlockedByPolicyIntegrationEvent evt,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<FileBlockedByPolicyIntegrationEvent> logger,
        CancellationToken ct
    ) =>
        RejectAsync(
            evt,
            "FileBlockedByPolicy",
            evt.FileId,
            "blocked_by_policy",
            repository,
            unitOfWork,
            bus,
            correlation,
            clock,
            logger,
            ct
        );

    private static async Task RejectAsync(
        IntegrationEvent evt,
        string source,
        Guid fileId,
        string reason,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var rejection = new StorageRejection(
                source,
                fileId,
                evt.TenantId,
                reason,
                ErrorCodeScanRejected,
                $"The generated file was rejected by the storage scan ({reason})."
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
                logger.LogDebug("{Source} {FileId} is not a Documents file; ignoring.", source, fileId);
        }
    }
}
