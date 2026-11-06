using BuildingBlocks.Domain;

namespace TaxVision.Codes.Domain.Events;

// Solo primitivos: la cola domain-events es durable y el evento se relee del sobre; Money y PaymentReference
// tienen constructor privado y no sobreviven al serializador.
public sealed record CodeReservationCreatedDomainEvent(
    Guid ReservationId,
    Guid TenantId,
    Guid QuoteId,
    Guid CodeDefinitionId,
    string PaymentSource,
    Guid PaymentId,
    DateTime OccurredAtUtc
) : IDomainEvent;
