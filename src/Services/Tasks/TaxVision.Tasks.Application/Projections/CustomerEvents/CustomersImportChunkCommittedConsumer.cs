using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Backfill;
using TaxVision.Tasks.Application.Backfill.Abstractions;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Domain.Projections;
using Wolverine;

namespace TaxVision.Tasks.Application.Projections.CustomerEvents;

/// <summary>
/// Un bloque guardado del import (hasta 500 clientes) con su ficha: nombre, email y estado desde el primer momento.
/// Un solo <c>MERGE</c> por bloque; la guarda de versión es la hora del evento, igual que en los eventos sueltos.
/// </summary>
public static class CustomersImportChunkCommittedConsumer
{
    public static async Task Handle(
        CustomersImportChunkCommittedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        ITenantCustomerBackfillService backfill,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CustomerDirectoryEntry> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            await TenantCustomerBackfillRequest.RequestIfNeededAsync(evt.TenantId, backfill, bus, ct);

            var entries = ToEntries(evt);
            if (entries.Count == 0)
                return;

            await repository.UpsertImportedAsync(evt.TenantId, entries, evt.OccurredOn, ct);
            logger.LogInformation(
                "CustomerDirectoryEntries upserted for tenant {TenantId}, import job {ImportJobId} chunk {ChunkNumber} ({Count} customers).",
                evt.TenantId,
                evt.ImportJobId,
                evt.ChunkNumber,
                entries.Count
            );
        }
    }

    // Una ficha por cliente (la última si se repite); estado desconocido cuenta como activo.
    private static List<ImportedDirectoryEntry> ToEntries(CustomersImportChunkCommittedIntegrationEvent evt) =>
        evt
            .Customers.GroupBy(card => card.CustomerId)
            .Select(group => group.Last())
            .Select(card => new ImportedDirectoryEntry(
                card.CustomerId,
                card.DisplayName,
                CustomerDirectoryEntry.NormalizeEmail(card.PrimaryEmail),
                Enum.TryParse<CustomerDirectoryStatus>(card.Status, ignoreCase: true, out var status)
                    ? status
                    : CustomerDirectoryStatus.Active
            ))
            .ToList();
}
