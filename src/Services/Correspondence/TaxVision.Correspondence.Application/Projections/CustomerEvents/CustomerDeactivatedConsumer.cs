using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Application.Projections.CustomerEvents;

/// <summary>
/// Desactivación del cliente ⇒ soft-delete de la proyección para que deje de matchear
/// como remitente conocido en el consumer de <c>raw_message_received</c> (Fase 4).
/// </summary>
public static class CustomerDeactivatedConsumer
{
    public static async Task Handle(
        CustomerDeactivatedIntegrationEvent evt,
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
                    "CustomerEmailAddress not found for {CustomerId}; nothing to deactivate.",
                    evt.CustomerId
                );
                return;
            }

            existing.SoftDelete();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
