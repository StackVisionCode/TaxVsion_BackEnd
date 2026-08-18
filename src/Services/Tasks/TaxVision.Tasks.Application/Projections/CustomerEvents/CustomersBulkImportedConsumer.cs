using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Backfill.Abstractions;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Application.Projections.CustomerEvents;

/// <summary>
/// El evento de import masivo sólo trae IDs, sin PII, así que las filas nuevas nacen sin nombre y el
/// job de reconciliación las completa después. Este consumer deduplica y trocea; el <c>MERGE</c>
/// set-based vive en el repositorio.
/// </summary>
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
