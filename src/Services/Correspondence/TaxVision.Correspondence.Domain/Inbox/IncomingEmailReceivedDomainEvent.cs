using BuildingBlocks.Domain;

namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>Entró un correo nuevo de un cliente. Lleva la foto: el aviso no relee el agregado.</summary>
public sealed record IncomingEmailReceivedDomainEvent(
    Guid TenantId,
    Guid IncomingEmailId,
    Guid CustomerId,
    Guid EmailThreadId,
    string Subject,
    Guid? MailboxAccountId,
    Guid? MailboxOwnerUserId,
    string CorrelationId
) : IDomainEvent;
