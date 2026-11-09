using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using TaxVision.Codes.Domain.Events;
using Wolverine;

namespace TaxVision.Growth.Api.DomainEvents;

/// <summary>
/// La reserva se confirmó: devuelve el aviso de integración (cascada por el outbox). Corre después del commit,
/// desde la cola domain-events. Todo lo que lleva el aviso viene en el evento; no relee el agregado.
/// </summary>
public static class CodeReservationCommittedDomainEventHandler
{
    public static CodeReservationCommittedIntegrationEvent Handle(
        CodeReservationCommittedDomainEvent committed,
        Envelope envelope
    ) =>
        new()
        {
            TenantId = committed.TenantId,
            AggregateId = committed.ReservationId,
            OccurredOn = committed.OccurredAtUtc,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            ReservationId = committed.ReservationId,
            RedemptionId = committed.RedemptionId,
            PaymentSource = committed.PaymentSource,
            RelatedPaymentId = committed.PaymentId,
            GrossAmountCents = committed.GrossAmountCents,
            DiscountAmountCents = committed.DiscountAmountCents,
            NetAmountCents = committed.NetAmountCents,
            Currency = committed.Currency,
        };
}
