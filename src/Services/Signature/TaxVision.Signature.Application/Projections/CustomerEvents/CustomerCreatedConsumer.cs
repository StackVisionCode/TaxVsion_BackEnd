using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CustomerEvents;

/// <summary>
/// Alta de cliente ⇒ inserta o reactiva la proyección local para poder resolver
/// <c>MappedCustomerId</c> al agregar firmantes por email (regla P-14).
/// Idempotente: si ya existe, se actualiza; si estaba archivado, se reactiva.
/// </summary>
public static class CustomerCreatedConsumer
{
    public static async Task Handle(
        CustomerCreatedIntegrationEvent evt,
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
                    "CustomerCreated event {EventId} for customer {CustomerId} has empty email; skipping projection.",
                    evt.EventId,
                    evt.CustomerId
                );
                return;
            }

            var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (existing is not null)
            {
                ReconcileExisting(existing, normalizedEmail, evt.DisplayName);
                logger.LogInformation(
                    "CustomerEmailProjection already exists for {CustomerId}; reconciled.",
                    evt.CustomerId
                );
            }
            else
            {
                var projection = CustomerEmailProjection.ForNewCustomer(
                    evt.TenantId,
                    evt.CustomerId,
                    normalizedEmail,
                    evt.DisplayName
                );
                await repository.AddAsync(projection, ct);
                logger.LogInformation(
                    "CustomerEmailProjection created for {CustomerId} (email={Email}).",
                    evt.CustomerId,
                    normalizedEmail
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string ResolveCorrelationId(CustomerCreatedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;

    private static string NormalizeEmail(string email) =>
        string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();

    private static void ReconcileExisting(CustomerEmailProjection existing, string normalizedEmail, string displayName)
    {
        if (existing.NormalizedEmail != normalizedEmail)
            existing.ChangeEmail(normalizedEmail);
        if (existing.DisplayName != displayName)
            existing.UpdateDisplayName(displayName);
        if (existing.IsArchived)
            existing.MarkReactivated();
    }
}
