using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Application.Projections.CustomerEvents;

/// <summary>
/// Reactivación de un cliente archivado ⇒ revierte el soft-delete de la proyección,
/// igual que Activated.
/// </summary>
public static class CustomerReactivatedConsumer
{
    public static async Task<LinkUnlinkedMailCommand?> Handle(
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
                return null;
            }

            // Igual que el alta: un email ya activo para otro cliente se omite en vez de violar el índice (DLQ).
            if (!existing.IsActive)
            {
                var emailOwner = await repository.FindActiveByAddressAsync(evt.TenantId, existing.EmailAddress, ct);
                if (emailOwner is not null && emailOwner.CustomerId != evt.CustomerId)
                {
                    logger.LogWarning(
                        "CustomerReactivated event {EventId}: email for customer {CustomerId} is already active for a different customer {ExistingCustomerId}; skipping reactivation.",
                        evt.EventId,
                        evt.CustomerId,
                        emailOwner.CustomerId
                    );
                    return null;
                }
            }

            if (!existing.Reactivate(evt.OccurredOn))
            {
                logger.LogDebug(
                    "{Event} {EventId} for customer {CustomerId} is older than the projection; ignored.",
                    "CustomerReactivated",
                    evt.EventId,
                    evt.CustomerId
                );
                return null;
            }
            await unitOfWork.SaveChangesAsync(ct);
            return new LinkUnlinkedMailCommand(evt.TenantId, evt.CustomerId, existing.EmailAddress);
        }
    }
}
