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
/// Alta de cliente ⇒ inserta la proyección local de email para que el consumer de
/// <c>raw_message_received</c> (Fase 4) pueda resolver el customer por remitente.
/// Idempotente: si ya existe la fila (llegada duplicada o fuera de orden), se reconcilia.
/// </summary>
public static class CustomerCreatedConsumer
{
    public static async Task<LinkUnlinkedMailCommand?> Handle(
        CustomerCreatedIntegrationEvent evt,
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
            // Único punto donde Correspondence "descubre" un tenant (no hay enumeración de
            // tenants M2M en el resto del monorepo) — no-op si ya corrió para este tenant.
            await backfill.EnsureBackfilledAsync(evt.TenantId, bus, ct);

            var emailResult = EmailAddress.Create(evt.PrimaryEmail);
            if (emailResult.IsFailure)
            {
                logger.LogWarning(
                    "CustomerCreated event {EventId} for customer {CustomerId} has an invalid email ({Error}); skipping projection.",
                    evt.EventId,
                    evt.CustomerId,
                    emailResult.Error.Code
                );
                return null;
            }

            var applied = await UpsertProjection(evt, emailResult.Value, repository, logger, ct);
            await unitOfWork.SaveChangesAsync(ct);

            // COR-05: lo que esta dirección escribió antes de ser cliente pasa a su ficha.
            return applied
                ? new LinkUnlinkedMailCommand(evt.TenantId, evt.CustomerId, emailResult.Value.NormalizedValue)
                : null;
        }
    }

    private static async Task<bool> UpsertProjection(
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
            // Igual que COR-06: un email activo de otro cliente se omite en vez de violar el índice (DLQ).
            var owner = await repository.FindActiveByAddressAsync(evt.TenantId, email.NormalizedValue, ct);
            if (owner is not null && owner.CustomerId != evt.CustomerId)
            {
                logger.LogWarning(
                    "CustomerCreated event {EventId}: email for customer {CustomerId} is already active for a different customer {ExistingCustomerId}; skipping reconcile.",
                    evt.EventId,
                    evt.CustomerId,
                    owner.CustomerId
                );
                return false;
            }

            existing.UpdateEmail(email, evt.OccurredOn);
            if (!existing.IsActive)
                existing.Reactivate(evt.OccurredOn);

            logger.LogInformation(
                "CustomerEmailAddress already exists for {CustomerId}; reconciled from Created event.",
                evt.CustomerId
            );
            return true;
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
                "CustomerCreated event {EventId}: email for customer {CustomerId} is already active for a different customer {ExistingCustomerId}; skipping projection.",
                evt.EventId,
                evt.CustomerId,
                emailOwner.CustomerId
            );
            return false;
        }

        var projection = CustomerEmailAddress.Create(evt.TenantId, evt.CustomerId, email, evt.OccurredOn);
        await repository.AddAsync(projection, ct);
        logger.LogInformation("CustomerEmailAddress created for {CustomerId}.", evt.CustomerId);
        return true;
    }
}
