using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;

namespace TaxVision.FinancialInsights.Application.Imports.Consumers;

/// <summary>
/// Resultado del escaneo de CloudStorage sobre un archivo ya registrado en un lote. EventId y FileId se mantienen en
/// la reentrega; la transición del agregado es idempotente (un segundo aviso no cambia nada).
/// </summary>
public static class FileScanConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        IImportBatchRepository batches,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt.CorrelationId, evt.EventId)))
        {
            if (!string.Equals(evt.OwnerType, "Customer", StringComparison.OrdinalIgnoreCase) || evt.OwnerId is null)
                return;
            var batch = await batches.GetByFileAsync(evt.TenantId, evt.FileId, ct);
            if (batch is null || batch.CustomerId != evt.OwnerId)
                return;
            if (batch.MarkFileReady(evt.FileId).IsSuccess)
                await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        IImportBatchRepository batches,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => RejectAsync(evt.TenantId, evt.FileId, "Infected", CorrelationOf(evt.CorrelationId, evt.EventId), batches, unitOfWork, correlation, ct);

    public static Task Handle(
        FileScanFailedIntegrationEvent evt,
        IImportBatchRepository batches,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => RejectAsync(evt.TenantId, evt.FileId, "ScanFailed", CorrelationOf(evt.CorrelationId, evt.EventId), batches, unitOfWork, correlation, ct);

    private static async Task RejectAsync(
        Guid tenantId,
        Guid fileId,
        string reason,
        string correlationId,
        IImportBatchRepository batches,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(correlationId))
        {
            var batch = await batches.GetByFileAsync(tenantId, fileId, ct);
            if (batch is not null && batch.RejectFile(fileId, reason).IsSuccess)
                await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
