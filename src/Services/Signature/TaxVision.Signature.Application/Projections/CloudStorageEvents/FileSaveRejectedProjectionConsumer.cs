using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Projections.CloudStorageEvents;

/// <summary>
/// CloudStorage descartó un guardado: la proyección queda <c>Rejected</c> para que quien espera el scan
/// (copias parciales, imágenes de firma) vea un resultado final. Igual que FileAvailable, se proyecta
/// sea cual sea el servicio que lo pidió: Signature puede leer archivos que subieron otros. Lo que
/// Signature hace con sus archivos finales lo resuelve <c>Sealing.FileSaveRejectedConsumer</c>.
/// </summary>
public static class FileSaveRejectedProjectionConsumer
{
    public static async Task Handle(
        FileSaveRejectedIntegrationEvent evt,
        IFileMetadataRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(RejectedFileProjection.CorrelationOf(evt.CorrelationId, evt.EventId)))
        {
            await RejectedFileProjection.RecordAsync(repository, evt.TenantId, evt.FileId, evt.Reason, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
