using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Application.Projections.CustomerEvents;

/// <summary>
/// Reactivación de un cliente archivado ⇒ revierte el soft-delete de la proyección,
/// igual que Activated.
/// </summary>
public static class CustomerReactivatedConsumer
{
    public static async Task Handle(
        CustomerReactivatedIntegrationEvent evt,
        ICustomerEmailAddressRepository repository,
        ITenantCustomerBackfillService backfill,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEmailAddress> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            await backfill.EnsureBackfilledAsync(evt.TenantId, ct);

            var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (existing is null)
            {
                logger.LogInformation(
                    "CustomerEmailAddress not found for {CustomerId}; nothing to reactivate.",
                    evt.CustomerId
                );
                return;
            }

            existing.Reactivate();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
