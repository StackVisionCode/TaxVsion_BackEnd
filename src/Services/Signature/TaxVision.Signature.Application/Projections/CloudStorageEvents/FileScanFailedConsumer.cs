using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Projections.CloudStorageEvents;

/// <summary>
/// El scan no aceptó el archivo (tipo de contenido no permitido o error del escáner): CloudStorage lo
/// marca ScanFailed y no lo reintenta solo, así que la proyección queda <c>Rejected</c>. Si un re-scan
/// manual lo deja disponible, el FileAvailable lo vuelve a marcar Available.
/// </summary>
public static class FileScanFailedConsumer
{
    public static async Task Handle(
        FileScanFailedIntegrationEvent evt,
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
