using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CloudStorageEvents;

/// <summary>
/// El archivo dio positivo en el scan de ClamAV. La proyección local queda marcada
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
                logger.LogInformation(
                    "FileInfectedDetected {FileId} but no local projection exists yet; nothing to update.",
                    evt.FileId
                );
                return;
            }

            existing.MarkInfected(evt.ScanReport);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string ResolveCorrelationId(FileInfectedDetectedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
