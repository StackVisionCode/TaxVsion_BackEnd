namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// El pago por HOSTED-CHECKOUT de una renovación/reactivación self-service falló o expiró sin pagar.
/// Subscription lo consume para marcar fallida la <c>SubscriptionRenewalIntent</c> (no cambia el estado de la
/// suscripción, que sigue en su lapso). Molde: <c>SeatsCheckoutFailedIntegrationEvent</c>.
/// </summary>
public sealed record SubscriptionRenewalCheckoutFailedIntegrationEvent : IntegrationEvent
{
    public required Guid RenewalIntentId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string FailureCode { get; init; }
    public required string FailureReason { get; init; }
}
