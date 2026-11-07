using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Backfill;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Domain.Projections;

namespace TaxVision.Calendar.Application.Projections.CustomerEvents;

public static class CustomerDeactivatedConsumer
{
    public static async Task<BackfillTenantCustomersCommand?> Handle(
        CustomerDeactivatedIntegrationEvent evt,
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
                    "CustomerDeactivated received but no CustomerDirectoryEntry exists for tenant {TenantId}, customer {CustomerId} — nothing to deactivate.",
                    evt.TenantId,
                    evt.CustomerId
                );
                return backfill;
            }

            existing.ApplyIfNewer(null, CustomerDirectoryStatus.Inactive, evt.DeactivatedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "CustomerDirectoryEntry deactivated for tenant {TenantId}, customer {CustomerId}.",
                evt.TenantId,
                evt.CustomerId
            );
            return backfill;
        }
    }
}
