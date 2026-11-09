using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Correspondence.Application.Projections.CustomerEvents;

/// <summary>
/// Actualización de cliente ⇒ sincroniza el email de la proyección local (upsert por
/// TenantId+CustomerId). Si la fila no existe (llegada fuera de orden respecto a
/// Created), se crea con el email actual.
/// </summary>
public static class CustomerUpdatedConsumer
{
    public static async Task<LinkUnlinkedMailCommand?> Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerEmailAddressRepository repository,
        ITenantCustomerBackfillService backfill,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEmailAddress> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            await backfill.EnsureBackfilledAsync(evt.TenantId, bus, ct);

            var emailResult = EmailAddress.Create(evt.PrimaryEmail);
            if (emailResult.IsFailure)
            {
                logger.LogWarning(
                    "CustomerUpdated event {EventId} for customer {CustomerId} has an invalid email ({Error}); skipping projection.",
                    evt.EventId,
                    evt.CustomerId,
                    emailResult.Error.Code
                );
                return null;
            }

            var emailEntered = await UpsertProjection(evt, emailResult.Value, repository, logger, ct);
            await unitOfWork.SaveChangesAsync(ct);

            return emailEntered
                ? new LinkUnlinkedMailCommand(evt.TenantId, evt.CustomerId, emailResult.Value.NormalizedValue)
                : null;
        }
    }

    // Devuelve true si la dirección entró a la proyección (COR-05).
    private static async Task<bool> UpsertProjection(
        CustomerUpdatedIntegrationEvent evt,
        EmailAddress email,
        ICustomerEmailAddressRepository repository,
        ILogger<CustomerEmailAddress> logger,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
        if (existing is null)
        {
            // Mismo riesgo de colisión que CustomerCreatedConsumer: IX_CustomerEmailAddresses_TenantId_EmailAddress_Active
            // es única por email activo dentro del tenant.
            var emailOwner = await repository.FindActiveByAddressAsync(evt.TenantId, email.NormalizedValue, ct);
            if (emailOwner is not null && emailOwner.CustomerId != evt.CustomerId)
            {
                logger.LogWarning(
                    "CustomerUpdated event {EventId}: email for customer {CustomerId} is already active for a different customer {ExistingCustomerId}; skipping back-create.",
                    evt.EventId,
                    evt.CustomerId,
                    emailOwner.CustomerId
                );
                return false;
            }

            var projection = CustomerEmailAddress.Create(evt.TenantId, evt.CustomerId, email, evt.OccurredOn);
            await repository.AddAsync(projection, ct);
            logger.LogInformation(
                "CustomerEmailAddress back-created for {CustomerId} from Updated event.",
                evt.CustomerId
            );
            return true;
        }

        var addressChanges = existing.EmailAddress != email.NormalizedValue;
        if (addressChanges)
        {
            var emailOwner = await repository.FindActiveByAddressAsync(evt.TenantId, email.NormalizedValue, ct);
            if (emailOwner is not null && emailOwner.CustomerId != evt.CustomerId)
            {
                logger.LogWarning(
                    "CustomerUpdated event {EventId}: new email for customer {CustomerId} is already active for a different customer {ExistingCustomerId}; skipping email sync.",
                    evt.EventId,
                    evt.CustomerId,
                    emailOwner.CustomerId
                );
                return false;
            }
        }

        if (!existing.UpdateEmail(email, evt.OccurredOn))
        {
            logger.LogDebug(
                "CustomerUpdated event {EventId} for customer {CustomerId} is older than the projection; ignored.",
                evt.EventId,
                evt.CustomerId
            );
            return false;
        }
        logger.LogInformation("CustomerEmailAddress synced for {CustomerId} from Updated event.", evt.CustomerId);
        return addressChanges;
    }
}
