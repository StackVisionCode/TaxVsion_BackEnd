using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando un cobro de renovación de seat (originado por
/// <see cref="SubscriptionIntegrationEvents.SeatRenewalDueIntegrationEvent"/>) se confirma
/// exitoso. Subscription lo consume para avanzar el período del seat.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.PaymentAppIntegrationEvents.SeatRenewalPaymentSucceededIntegrationEvent")]
public sealed record SeatRenewalPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid SeatId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string ExternalPaymentReference { get; init; }
    public required DateTime PaidAtUtc { get; init; }
}
