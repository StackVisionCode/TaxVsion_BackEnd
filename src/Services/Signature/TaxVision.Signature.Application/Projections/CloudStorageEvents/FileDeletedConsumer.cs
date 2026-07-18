using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CloudStorageEvents;

/// <summary>
/// El archivo fue eliminado en CloudStorage. La proyección local queda marcada como
/// <c>Deleted</c> para conservar trazabilidad histórica.
/// </summary>
public static class FileDeletedConsumer
{
    public static async Task Handle(
        FileDeletedIntegrationEvent evt,
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
                    "FileDeleted {FileId} but no local projection exists; nothing to update.",
                    evt.FileId
                );
                return;
            }

            existing.MarkDeleted();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string ResolveCorrelationId(FileDeletedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
