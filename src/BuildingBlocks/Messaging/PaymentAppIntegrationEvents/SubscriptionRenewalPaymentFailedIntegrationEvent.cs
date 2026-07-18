namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando un cobro de renovación de suscripción falla (originado por
/// <see cref="SubscriptionIntegrationEvents.SubscriptionRenewalDueIntegrationEvent"/>).
/// Subscription lo consume para transicionar a PastDue / agendar dunning.
/// </summary>
public sealed record SubscriptionRenewalPaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid TenantSubscriptionId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string FailureCode { get; init; }
    public required string FailureReason { get; init; }
    public required bool WillRetry { get; init; }
    public DateTime? NextRetryAtUtc { get; init; }
}
