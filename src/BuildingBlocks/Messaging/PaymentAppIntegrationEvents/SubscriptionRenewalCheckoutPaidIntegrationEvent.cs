namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// El pago por HOSTED-CHECKOUT de una renovación/reactivación self-service de la suscripción base se confirmó
/// (webhook del provider). Subscription lo consume para REACTIVAR la suscripción (PastDue/GracePeriod/Suspended/
/// Expired → Active con período nuevo) sin volver a cobrar — el cobro ya lo hizo el checkout.
/// <see cref="RenewalIntentId"/> (= <c>SaaSPayment.TargetAggregateId</c>) correlaciona con la
/// <c>SubscriptionRenewalIntent</c> de Subscription. Molde: <c>SeatsCheckoutPaidIntegrationEvent</c>.
/// </summary>
public sealed record SubscriptionRenewalCheckoutPaidIntegrationEvent : IntegrationEvent
{
    public required Guid RenewalIntentId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required long AmountPaidCents { get; init; }
    public required string Currency { get; init; }
    public required DateTime PaidAtUtc { get; init; }
    public required string ProviderPaymentReference { get; init; }
}
