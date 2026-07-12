using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CustomerEvents;

/// <summary>
/// Actualización de cliente ⇒ sincroniza email y nombre en la proyección local.
/// Si no existe la fila (llegada fuera de orden), se crea con los datos actuales.
/// </summary>
public static class CustomerUpdatedConsumer
{
    public static async Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerEmailProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEmailProjection> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            var normalizedEmail = NormalizeEmail(evt.PrimaryEmail);
            if (string.IsNullOrEmpty(normalizedEmail))
            {
                logger.LogWarning(
                    "CustomerUpdated event {EventId} for customer {CustomerId} has empty email; skipping projection.",
                    evt.EventId,
                    evt.CustomerId
                );
                return;
            }

            var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (existing is null)
            {
                var projection = CustomerEmailProjection.ForNewCustomer(
                    evt.TenantId,
                    evt.CustomerId,
                    normalizedEmail,
                    evt.DisplayName
                );
                await repository.AddAsync(projection, ct);
                logger.LogInformation(
                    "CustomerEmailProjection back-created for {CustomerId} from Updated event.",
                    evt.CustomerId
                );
            }
            else
            {
                ApplyChanges(existing, normalizedEmail, evt.DisplayName);
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string ResolveCorrelationId(CustomerUpdatedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;

    private static string NormalizeEmail(string email) =>
        string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();

    private static void ApplyChanges(CustomerEmailProjection existing, string normalizedEmail, string displayName)
    {
        if (existing.NormalizedEmail != normalizedEmail)
            existing.ChangeEmail(normalizedEmail);
        if (existing.DisplayName != displayName)
            existing.UpdateDisplayName(displayName);
    }
}
