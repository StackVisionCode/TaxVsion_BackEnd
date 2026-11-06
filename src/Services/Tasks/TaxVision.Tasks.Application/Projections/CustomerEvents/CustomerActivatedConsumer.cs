using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Backfill.Abstractions;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Application.Projections.CustomerEvents;

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
            // Llegó antes que Created: se reintenta con espera (y a la DLQ si nunca llega), no se descarta.
            if (existing is null)
                throw new DependencyNotReadyException(
                    "CustomerDirectoryEntry",
                    $"tenant {evt.TenantId}, customer {evt.CustomerId}"
                );

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
