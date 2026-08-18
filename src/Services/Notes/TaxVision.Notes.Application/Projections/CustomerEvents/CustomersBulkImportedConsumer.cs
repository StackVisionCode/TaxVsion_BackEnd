using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.CustomerEvents;

// ---------------------------------------------------------------------------
// Fase 4B — CustomersBulkImportedIntegrationEvent no trae PII (solo IDs, ver
// BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomersBulkImportedIntegrationEvent), así
// que las filas nuevas creadas por este consumer nacen con DisplayName=NULL — el mismo estado
// que produce un miss de nombre en cualquier otro flujo, y se cierra después por
// CustomerDirectoryReconciliationJob. Guardrail del plan (03_Plan_De_Fases.md §4B): "Usar MERGE
// (raw SQL) — NUNCA cargar N entidades ni fetch por id" — UpsertBulkAsync ya cumple esto
// (CustomerDirectoryRepository.cs), este consumer solo dedupe + chunkea antes de invocarlo.
// ---------------------------------------------------------------------------

public static class CustomersBulkImportedConsumer
{
    private const int ChunkSize = 500;

    public static async Task Handle(
        CustomersBulkImportedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        ITenantCustomerBackfillService backfill,
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
            await backfill.EnsureBackfilledAsync(evt.TenantId, ct);

            var customerIds = evt.CreatedCustomerIds.Concat(evt.UpdatedCustomerIds).Distinct().ToArray();

            if (customerIds.Length == 0)
                return;

            foreach (var chunk in customerIds.Chunk(ChunkSize))
                await repository.UpsertBulkAsync(evt.TenantId, chunk, evt.CompletedAtUtc, ct);

            logger.LogInformation(
                "CustomerDirectoryEntries bulk-upserted for tenant {TenantId}, import job {ImportJobId} ({Count} customer ids).",
                evt.TenantId,
                evt.ImportJobId,
                customerIds.Length
            );
        }
    }
}
