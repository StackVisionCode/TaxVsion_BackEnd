using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Documents.Application.Abstractions;
using Wolverine;

namespace TaxVision.Documents.Application.Generations;

/// <summary>Dueño y carpeta con que CloudStorage registra un archivo generado.</summary>
public sealed record GeneratedFileDestination(
    Guid TenantId,
    string FileName,
    string OwnerType,
    Guid OwnerId,
    string FolderType,
    int? TaxYear,
    Guid ActorId,
    string CorrelationId
);

/// <summary>
/// Pide a CloudStorage que guarde lo subido. Recibe el bus del handler: así el mensaje entra en su transacción
/// y sale con el commit, cuando la generación con ese FileId ya existe (DOC-3).
/// </summary>
internal static class GeneratedFileHandoff
{
    public static ValueTask RequestSaveAsync(
        this IMessageBus bus,
        StorageUploadResult upload,
        GeneratedFileDestination destination
    ) =>
        bus.PublishAsync(
            new SaveFileRequestedIntegrationEvent
            {
                TenantId = destination.TenantId,
                CorrelationId = destination.CorrelationId,
                FileId = upload.FileId,
                RequestingService = IDocumentStorageClient.RequestingService,
                SourceBucket = upload.Bucket,
                SourceObjectKey = upload.ObjectKey,
                ActorId = destination.ActorId,
                OwnerType = destination.OwnerType,
                OwnerId = destination.OwnerId,
                FolderType = destination.FolderType,
                TaxYear = destination.TaxYear,
                OriginalName = destination.FileName,
                ContentType = upload.ContentType,
                SizeBytes = upload.SizeBytes,
            }
        );
}
