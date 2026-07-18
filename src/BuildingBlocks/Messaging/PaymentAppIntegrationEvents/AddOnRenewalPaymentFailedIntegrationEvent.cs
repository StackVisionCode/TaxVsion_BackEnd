namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando un cobro de renovación de add-on falla (originado por
/// <see cref="SubscriptionIntegrationEvents.AddOnRenewalDueIntegrationEvent"/>). Subscription
/// lo consume para transicionar el add-on a PastDue / agendar dunning.
/// </summary>
public sealed record AddOnRenewalPaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid TenantAddOnId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string FailureCode { get; init; }
    public required string FailureReason { get; init; }
    public required bool WillRetry { get; init; }
    public DateTime? NextRetryAtUtc { get; init; }
}
