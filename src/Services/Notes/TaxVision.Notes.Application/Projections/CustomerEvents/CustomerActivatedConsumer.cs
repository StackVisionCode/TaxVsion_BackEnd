using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.CustomerEvents;

public static class CustomerActivatedConsumer
{
    public static async Task Handle(
        CustomerActivatedIntegrationEvent evt,
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
                    "CustomerActivated received but no CustomerDirectoryEntry exists for tenant {TenantId}, customer {CustomerId} — nothing to activate.",
                    evt.TenantId,
                    evt.CustomerId
                );
                return;
            }

            existing.ApplyIfNewer(null, CustomerDirectoryStatus.Active, evt.ActivatedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "CustomerDirectoryEntry activated for tenant {TenantId}, customer {CustomerId}.",
                evt.TenantId,
                evt.CustomerId
            );
        }
    }
}
