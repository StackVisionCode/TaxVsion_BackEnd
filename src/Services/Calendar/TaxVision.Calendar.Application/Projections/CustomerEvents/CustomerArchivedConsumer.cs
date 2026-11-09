using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Backfill;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Domain.Projections;

namespace TaxVision.Calendar.Application.Projections.CustomerEvents;

public static class CustomerArchivedConsumer
{
    public static async Task<BackfillTenantCustomersCommand?> Handle(
        CustomerArchivedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        ITenantBackfillStateRepository backfillStates,
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
            var backfill = await BackfillTenantCustomersCommand.IfMissingAsync(evt.TenantId, backfillStates, ct);

            var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (existing is null)
            {
                logger.LogWarning(
                    "CustomerArchived received but no CustomerDirectoryEntry exists for tenant {TenantId}, customer {CustomerId} — nothing to archive.",
                    evt.TenantId,
                    evt.CustomerId
                );
                return backfill;
            }

            // Este evento no trae fecha propia, a diferencia de sus hermanos: se usa la de ocurrencia.
            existing.ApplyIfNewer(null, CustomerDirectoryStatus.Archived, evt.OccurredOn);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "CustomerDirectoryEntry archived for tenant {TenantId}, customer {CustomerId}.",
                evt.TenantId,
                evt.CustomerId
            );
            return backfill;
        }
    }
}
