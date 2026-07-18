using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Application.Projections.CustomerEvents;

/// <summary>
/// Actualización de cliente ⇒ sincroniza el email de la proyección local (upsert por
/// TenantId+CustomerId). Si la fila no existe (llegada fuera de orden respecto a
/// Created), se crea con el email actual.
/// </summary>
public static class CustomerUpdatedConsumer
{
    public static async Task Handle(
        CustomerUpdatedIntegrationEvent evt,
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

            var emailResult = EmailAddress.Create(evt.PrimaryEmail);
            if (emailResult.IsFailure)
            {
                logger.LogWarning(
                    "CustomerUpdated event {EventId} for customer {CustomerId} has an invalid email ({Error}); skipping projection.",
                    evt.EventId,
                    evt.CustomerId,
                    emailResult.Error.Code
                );
                return;
            }

            await UpsertProjection(evt, emailResult.Value, repository, logger, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static async Task UpsertProjection(
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
            var projection = CustomerEmailAddress.Create(evt.TenantId, evt.CustomerId, email);
            await repository.AddAsync(projection, ct);
            logger.LogInformation(
                "CustomerEmailAddress back-created for {CustomerId} from Updated event.",
                evt.CustomerId
            );
            return;
        }

        existing.UpdateEmail(email);
        logger.LogInformation("CustomerEmailAddress synced for {CustomerId} from Updated event.", evt.CustomerId);
    }
}
