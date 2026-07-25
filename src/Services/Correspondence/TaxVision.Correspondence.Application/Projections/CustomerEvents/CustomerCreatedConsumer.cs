using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Application.Projections.CustomerEvents;

/// <summary>
/// Alta de cliente ⇒ inserta la proyección local de email para que el consumer de
/// <c>raw_message_received</c> (Fase 4) pueda resolver el customer por remitente.
/// Idempotente: si ya existe la fila (llegada duplicada o fuera de orden), se reconcilia.
/// </summary>
public static class CustomerCreatedConsumer
{
    public static async Task Handle(
        CustomerCreatedIntegrationEvent evt,
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
            // Único punto donde Correspondence "descubre" un tenant (no hay enumeración de
            // tenants M2M en el resto del monorepo) — no-op si ya corrió para este tenant.
            await backfill.EnsureBackfilledAsync(evt.TenantId, ct);

            var emailResult = EmailAddress.Create(evt.PrimaryEmail);
            if (emailResult.IsFailure)
            {
                logger.LogWarning(
                    "CustomerCreated event {EventId} for customer {CustomerId} has an invalid email ({Error}); skipping projection.",
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
        CustomerCreatedIntegrationEvent evt,
        EmailAddress email,
        ICustomerEmailAddressRepository repository,
        ILogger<CustomerEmailAddress> logger,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
        if (existing is not null)
        {
            existing.UpdateEmail(email);
            if (!existing.IsActive)
                existing.Reactivate();

            logger.LogInformation(
                "CustomerEmailAddress already exists for {CustomerId}; reconciled from Created event.",
                evt.CustomerId
            );
            return;
        }

        // IX_CustomerEmailAddresses_TenantId_EmailAddress_Active es única por email activo dentro
        // del tenant — dos customers distintos con el mismo email (p.ej. re-alta con un email ya
        // usado por otro cliente) violarían el índice y tirarían el evento entero a la DLQ. Resolver
        // a qué customer pertenece un email compartido es una decisión de negocio pendiente; por
        // ahora se registra y se omite la proyección en vez de crashear el consumer.
        var emailOwner = await repository.FindActiveByAddressAsync(evt.TenantId, email.NormalizedValue, ct);
        if (emailOwner is not null && emailOwner.CustomerId != evt.CustomerId)
        {
            logger.LogWarning(
                "CustomerCreated event {EventId}: email {Email} for customer {CustomerId} is already active for a different customer {ExistingCustomerId}; skipping projection.",
                evt.EventId,
                email.NormalizedValue,
                evt.CustomerId,
                emailOwner.CustomerId
            );
            return;
        }

        var projection = CustomerEmailAddress.Create(evt.TenantId, evt.CustomerId, email);
        await repository.AddAsync(projection, ct);
        logger.LogInformation("CustomerEmailAddress created for {CustomerId}.", evt.CustomerId);
    }
}
