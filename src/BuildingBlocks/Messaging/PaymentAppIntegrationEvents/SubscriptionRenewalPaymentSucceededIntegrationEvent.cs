namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando un cobro de renovación de suscripción (originado por
/// <see cref="SubscriptionIntegrationEvents.SubscriptionRenewalDueIntegrationEvent"/>) se
/// confirma exitoso. Subscription lo consume para avanzar el período de la suscripción.
/// </summary>
public sealed record SubscriptionRenewalPaymentSucceededIntegrationEvent : IntegrationEvent
{
    public required Guid TenantSubscriptionId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string ExternalPaymentReference { get; init; }
    public required DateTime PaidAtUtc { get; init; }
}
