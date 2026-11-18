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

            // E-3: si la fila nace tarde (el email llegó después del alta) y el cliente ya no está activo, nace
            // inactiva como la habría dejado su Deactivated/Archived, que no encontró fila.
            if (
                IsInactive(evt.Status)
                && await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct) is null
            )
            {
                var inactive = CustomerEmailAddress.Create(
                    evt.TenantId,
                    evt.CustomerId,
                    emailResult.Value,
                    evt.OccurredOn
                );
                inactive.SoftDelete(evt.OccurredOn);
                await repository.AddAsync(inactive, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return null;
            }

            var emailEntered = await UpsertProjection(
                evt.TenantId,
                evt.CustomerId,
                evt.EventId,
                evt.OccurredOn,
                emailResult.Value,
                repository,
                logger,
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);

            return emailEntered
                ? new LinkUnlinkedMailCommand(evt.TenantId, evt.CustomerId, emailResult.Value.NormalizedValue)
                : null;
        }
    }

    private static bool IsInactive(string? status) =>
        !string.IsNullOrWhiteSpace(status) && !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    // Devuelve true si la dirección entró a la proyección (COR-05).
    internal static async Task<bool> UpsertProjection(
        Guid tenantId,
        Guid customerId,
        Guid eventId,
        DateTime occurredOn,
        EmailAddress email,
        ICustomerEmailAddressRepository repository,
        ILogger<CustomerEmailAddress> logger,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(tenantId, customerId, ct);
        if (existing is null)
        {
            // Mismo riesgo de colisión que CustomerCreatedConsumer: IX_CustomerEmailAddresses_TenantId_EmailAddress_Active
            // es única por email activo dentro del tenant.
            var emailOwner = await repository.FindActiveByAddressAsync(tenantId, email.NormalizedValue, ct);
            if (emailOwner is not null && emailOwner.CustomerId != customerId)
            {
                logger.LogWarning(
                    "CustomerUpdated event {EventId}: email for customer {CustomerId} is already active for a different customer {ExistingCustomerId}; skipping back-create.",
                    eventId,
                    customerId,
                    emailOwner.CustomerId
                );
                return false;
            }

            var projection = CustomerEmailAddress.Create(tenantId, customerId, email, occurredOn);
            await repository.AddAsync(projection, ct);
            logger.LogInformation("CustomerEmailAddress back-created for {CustomerId} from Updated event.", customerId);
            return true;
        }

        var addressChanges = existing.EmailAddress != email.NormalizedValue;
        if (addressChanges)
        {
            var emailOwner = await repository.FindActiveByAddressAsync(tenantId, email.NormalizedValue, ct);
            if (emailOwner is not null && emailOwner.CustomerId != customerId)
            {
                logger.LogWarning(
                    "CustomerUpdated event {EventId}: new email for customer {CustomerId} is already active for a different customer {ExistingCustomerId}; skipping email sync.",
                    eventId,
                    customerId,
                    emailOwner.CustomerId
                );
                return false;
            }
        }

        if (!existing.UpdateEmail(email, occurredOn))
        {
            logger.LogDebug(
                "CustomerUpdated event {EventId} for customer {CustomerId} is older than the projection; ignored.",
                eventId,
                customerId
            );
            return false;
        }
        logger.LogInformation("CustomerEmailAddress synced for {CustomerId} from Updated event.", customerId);
        return addressChanges;
    }
}
