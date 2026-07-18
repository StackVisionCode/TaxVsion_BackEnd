namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando un cobro de renovación de add-on (originado por
/// <see cref="SubscriptionIntegrationEvents.AddOnRenewalDueIntegrationEvent"/>) se confirma
/// exitoso. Subscription lo consume para avanzar el período del add-on.
/// </summary>
public sealed record AddOnRenewalPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid TenantAddOnId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string ExternalPaymentReference { get; init; }
    public required DateTime PaidAtUtc { get; init; }
}
