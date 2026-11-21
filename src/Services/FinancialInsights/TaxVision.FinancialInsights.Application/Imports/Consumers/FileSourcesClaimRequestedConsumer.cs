using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using Wolverine;

namespace TaxVision.FinancialInsights.Application.Imports.Consumers;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen: FinancialInsights declara solo los documentos de sus
/// lotes, como estado de cuenta del lote (igual que el backfill manual). Los ids ajenos se ignoran. Sale por outbox.
/// </summary>
public static class FileSourcesClaimRequestedConsumer
{
    public static async Task Handle(
        FileSourcesClaimRequestedIntegrationEvent evt,
        IImportBatchRepository batches,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (evt.TenantId == Guid.Empty || evt.FileIds.Count == 0)
            return;

        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var claimed = evt.FileIds.Take(FileSourcesClaimRequestedIntegrationEvent.MaxFileIds).ToHashSet();
            var files = await batches.ListFilesByIdsAsync(evt.TenantId, claimed, ct);
            foreach (var file in files)
                await bus.PublishAsync(
                    ImportFileSources.Declaration(
                        evt.TenantId,
                        file.BatchId,
                        file.CustomerId,
                        file.FileId,
                        correlation.CorrelationId
                    )
                );
        }
    }
}
