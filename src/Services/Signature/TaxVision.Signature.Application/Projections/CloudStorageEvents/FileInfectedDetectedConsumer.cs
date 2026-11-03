using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CloudStorageEvents;

/// <summary>
/// El archivo dio positivo en el scan de ClamAV. La proyección local queda marcada (o se crea)
/// como <c>Infected</c> para que ninguna solicitud lo tome como válido.
/// </summary>
public static class FileInfectedDetectedConsumer
{
    public static async Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        IFileMetadataRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<FileMetadataRef> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            var existing = await repository.GetByFileIdAsync(evt.TenantId, evt.FileId, ct);
            if (existing is null)
            {
                // Sin fila previa (nunca hubo FileAvailable): se crea ya infectada para que quien espera el
                // scan vea un resultado final en vez de esperar hasta su tope.
                logger.LogInformation(
                    "FileInfectedDetected {FileId} with no local projection; recording it.",
                    evt.FileId
                );
                await repository.AddAsync(FileMetadataRef.ForInfected(evt.TenantId, evt.FileId, evt.ScanReport), ct);
            }
            else
                existing.MarkInfected(evt.ScanReport);

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string ResolveCorrelationId(FileInfectedDetectedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
