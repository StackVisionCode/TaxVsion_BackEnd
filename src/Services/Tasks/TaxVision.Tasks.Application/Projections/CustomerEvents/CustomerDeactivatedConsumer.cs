using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Backfill.Abstractions;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Application.Projections.CustomerEvents;

public static class CustomerDeactivatedConsumer
{
    public static async Task Handle(
        CustomerDeactivatedIntegrationEvent evt,
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
                    "CustomerDeactivated received but no CustomerDirectoryEntry exists for tenant {TenantId}, customer {CustomerId} — nothing to deactivate.",
                    evt.TenantId,
                    evt.CustomerId
                );
                return;
            }

            existing.ApplyIfNewer(null, CustomerDirectoryStatus.Inactive, evt.DeactivatedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "CustomerDirectoryEntry deactivated for tenant {TenantId}, customer {CustomerId}.",
                evt.TenantId,
                evt.CustomerId
            );
        }
    }
}
