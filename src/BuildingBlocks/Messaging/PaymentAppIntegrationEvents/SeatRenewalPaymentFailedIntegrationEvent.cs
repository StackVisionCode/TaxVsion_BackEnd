namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando un cobro de renovación de seat falla (originado por
/// <see cref="SubscriptionIntegrationEvents.SeatRenewalDueIntegrationEvent"/>). Subscription
/// lo consume para transicionar el seat a PastDue / agendar dunning.
/// </summary>
public sealed record SeatRenewalPaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid SeatId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string FailureCode { get; init; }
    public required string FailureReason { get; init; }
    public required bool WillRetry { get; init; }
    public DateTime? NextRetryAtUtc { get; init; }
}
