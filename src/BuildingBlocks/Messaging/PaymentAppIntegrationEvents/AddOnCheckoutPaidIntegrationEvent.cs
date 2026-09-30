namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// El pago por HOSTED-CHECKOUT de la compra de un add-on se confirmó (webhook del provider). Subscription lo
/// consume para ACTIVAR el add-on sin volver a cobrar — el cobro ya lo hizo el checkout.
/// <see cref="AddOnPurchaseIntentId"/> (= <c>SaaSPayment.TargetAggregateId</c>) correlaciona con la
/// <c>AddOnPurchaseIntent</c> de Subscription, que guarda qué add-on, cuántos, auto-renovación y precio.
/// Distinto de <see cref="AddOnRenewalPaymentSucceededIntegrationEvent"/>, que es la renovación off-session
/// de un add-on que ya existe. Alias v1.
/// </summary>
public sealed record AddOnCheckoutPaidIntegrationEvent : IntegrationEvent
{
    public required Guid AddOnPurchaseIntentId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required long AmountPaidCents { get; init; }
    public required string Currency { get; init; }
    public required DateTime PaidAtUtc { get; init; }
    public required string ProviderPaymentReference { get; init; }
}
