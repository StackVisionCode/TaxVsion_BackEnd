using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Inbox.DomainEvents;

/// <summary>
/// Entró un correo de un cliente: devuelve el aviso de integración (cascada por el outbox). Corre después del commit,
/// desde la cola domain-events. Todo lo que lleva el aviso viene en el evento; no relee el agregado.
/// </summary>
public static class IncomingEmailReceivedDomainEventHandler
{
    public static CorrespondenceCustomerEmailReceivedIntegrationEvent Handle(
        IncomingEmailReceivedDomainEvent received
    ) =>
        new()
        {
            TenantId = received.TenantId,
            CorrelationId = received.CorrelationId,
            CustomerId = received.CustomerId,
            IncomingEmailId = received.IncomingEmailId,
            EmailThreadId = received.EmailThreadId,
            Subject = received.Subject,
            AccountId = received.MailboxAccountId,
            MailboxOwnerUserId = received.MailboxOwnerUserId,
        };
}
