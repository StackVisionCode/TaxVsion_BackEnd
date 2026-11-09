using BuildingBlocks.Domain;

namespace TaxVision.Codes.Domain.Events;

// Solo primitivos: la cola domain-events es durable y el evento se relee del sobre; Money y PaymentReference
// tienen constructor privado y no sobreviven al serializador.
public sealed record CodeReservationCommittedDomainEvent(
    Guid ReservationId,
    Guid TenantId,
    Guid RedemptionId,
    string PaymentSource,
    Guid PaymentId,
    long GrossAmountCents,
    long DiscountAmountCents,
    long NetAmountCents,
    string Currency,
    bool WasLateCommit,
    DateTime OccurredAtUtc
) : IDomainEvent;
