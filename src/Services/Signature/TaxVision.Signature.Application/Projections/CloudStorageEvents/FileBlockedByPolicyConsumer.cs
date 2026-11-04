using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Projections.CloudStorageEvents;

/// <summary>La moderación bloqueó el archivo (cuarentena): la proyección queda <c>Rejected</c> con el motivo.</summary>
public static class FileBlockedByPolicyConsumer
{
    public static async Task Handle(
        FileBlockedByPolicyIntegrationEvent evt,
        IFileMetadataRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(RejectedFileProjection.CorrelationOf(evt.CorrelationId, evt.EventId)))
        {
            await RejectedFileProjection.RecordAsync(repository, evt.TenantId, evt.FileId, evt.PolicyReason, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
