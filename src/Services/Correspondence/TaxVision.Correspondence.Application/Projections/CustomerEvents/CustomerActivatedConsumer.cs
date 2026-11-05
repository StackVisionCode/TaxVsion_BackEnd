using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Application.Projections.CustomerEvents;

/// <summary>
/// Reactivación del cliente ⇒ revierte el soft-delete de la proyección para que vuelva
/// a matchear como remitente conocido.
/// </summary>
public static class CustomerActivatedConsumer
{
    public static async Task Handle(
        CustomerActivatedIntegrationEvent evt,
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
                    "CustomerEmailAddress not found for {CustomerId}; nothing to activate.",
                    evt.CustomerId
                );
                return;
            }

            // Igual que el alta: un email ya activo para otro cliente se omite en vez de violar el índice (DLQ).
            if (!existing.IsActive)
            {
                var emailOwner = await repository.FindActiveByAddressAsync(evt.TenantId, existing.EmailAddress, ct);
                if (emailOwner is not null && emailOwner.CustomerId != evt.CustomerId)
                {
                    logger.LogWarning(
                        "CustomerActivated event {EventId}: email for customer {CustomerId} is already active for a different customer {ExistingCustomerId}; skipping reactivation.",
                        evt.EventId,
                        evt.CustomerId,
                        emailOwner.CustomerId
                    );
                    return;
                }
            }

            if (!existing.Reactivate(evt.OccurredOn))
            {
                logger.LogDebug(
                    "{Event} {EventId} for customer {CustomerId} is older than the projection; ignored.",
                    "CustomerActivated",
                    evt.EventId,
                    evt.CustomerId
                );
                return;
            }
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
