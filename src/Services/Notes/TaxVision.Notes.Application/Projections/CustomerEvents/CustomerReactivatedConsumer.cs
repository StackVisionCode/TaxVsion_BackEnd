using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Backfill;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.CustomerEvents;

public static class CustomerReactivatedConsumer
{
    public static async Task<RunTenantCustomerBackfillCommand?> Handle(
        CustomerReactivatedIntegrationEvent evt,
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
            var backfill = await RunTenantCustomerBackfillCommand.IfMissingAsync(evt.TenantId, backfillStates, ct);

            var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (existing is null)
            {
                logger.LogWarning(
                    "CustomerReactivated received but no CustomerDirectoryEntry exists for tenant {TenantId}, customer {CustomerId} — nothing to reactivate.",
                    evt.TenantId,
                    evt.CustomerId
                );
                return backfill;
            }

            // Reactivated fires Archived -> Active (ver contrato del evento en Customer).
            existing.ApplyIfNewer(null, CustomerDirectoryStatus.Active, evt.ReactivatedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "CustomerDirectoryEntry reactivated for tenant {TenantId}, customer {CustomerId}.",
                evt.TenantId,
                evt.CustomerId
            );
            return backfill;
        }
    }
}
