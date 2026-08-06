using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.CustomerEvents;

public static class CustomerArchivedConsumer
{
    public static async Task Handle(
        CustomerArchivedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        ITenantCustomerBackfillService backfill,
        IUnitOfWork unitOfWork,
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

            var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (existing is null)
            {
                logger.LogWarning(
                    "CustomerArchived received but no CustomerDirectoryEntry exists for tenant {TenantId}, customer {CustomerId} — nothing to archive.",
                    evt.TenantId,
                    evt.CustomerId
                );
                return;
            }

            // CustomerArchivedIntegrationEvent no trae un ArchivedAtUtc propio (a diferencia de sus
            // hermanos Activated/Deactivated/Reactivated) — se usa OccurredOn como observación.
            existing.ApplyIfNewer(null, CustomerDirectoryStatus.Archived, evt.OccurredOn);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "CustomerDirectoryEntry archived for tenant {TenantId}, customer {CustomerId}.",
                evt.TenantId,
                evt.CustomerId
            );
        }
    }
}
