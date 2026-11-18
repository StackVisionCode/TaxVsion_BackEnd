using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Backfill;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.CustomerEvents;

/// <summary>
/// Un bloque guardado del import (hasta 500 clientes) con su ficha: el nombre llega desde el primer momento. Un solo
/// <c>MERGE</c> por bloque; la guarda de versión es la hora del evento, igual que en los eventos sueltos.
/// </summary>
public static class CustomersImportChunkCommittedConsumer
{
    public static async Task<RunTenantCustomerBackfillCommand?> Handle(
        CustomersImportChunkCommittedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        ITenantBackfillStateRepository backfillStates,
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
            var backfill = await RunTenantCustomerBackfillCommand.IfMissingAsync(evt.TenantId, backfillStates, ct);

            var entries = ToEntries(evt);
            if (entries.Count == 0)
                return backfill;

            await repository.UpsertImportedAsync(evt.TenantId, entries, evt.OccurredOn, ct);
            logger.LogInformation(
                "CustomerDirectoryEntries upserted for tenant {TenantId}, import job {ImportJobId} chunk {ChunkNumber} ({Count} customers).",
                evt.TenantId,
                evt.ImportJobId,
                evt.ChunkNumber,
                entries.Count
            );
            return backfill;
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
                Enum.TryParse<CustomerDirectoryStatus>(card.Status, ignoreCase: true, out var status)
                    ? status
                    : CustomerDirectoryStatus.Active
            ))
            .ToList();
}
