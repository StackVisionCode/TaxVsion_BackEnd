using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Billing.Application.Abstractions;
using Wolverine;

namespace TaxVision.Billing.Application.Invoices.IntegrationEvents;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen: Billing declara solo los PDF vigentes de sus facturas
/// que estén en la lista, con los mismos valores que el backfill manual. Los ids ajenos se ignoran. Sale por outbox.
/// </summary>
public static class FileSourcesClaimRequestedConsumer
{
    public static async Task Handle(
        FileSourcesClaimRequestedIntegrationEvent evt,
        IInvoicePdfFileReader reader,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider clock,
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
            var rows = await reader.ListByFileIdsAsync(evt.TenantId, claimed, ct);
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            var declarations = rows.SelectMany(row =>
                    InvoicePdfSource.DeclareCurrent(evt.TenantId, row, nowUtc, correlation.CorrelationId)
                )
                .Where(declaration => claimed.Contains(declaration.FileId));
            foreach (var declaration in declarations)
                await bus.PublishAsync(declaration);
        }
    }
}
