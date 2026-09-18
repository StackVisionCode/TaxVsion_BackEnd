namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// El pago por HOSTED-CHECKOUT de una compra de asientos extra se confirmó (webhook del provider). Subscription
/// lo consume para APROVISIONAR los asientos (crear + activar) sin volver a cobrar — el cobro ya lo hizo el
/// checkout. <see cref="SeatPurchaseIntentId"/> (= <c>SaaSPayment.TargetAggregateId</c>) correlaciona con la
/// <c>SeatPurchaseIntent</c> de Subscription que guarda tipo/cantidad/auto-renovación/precio. Alias v1.
/// </summary>
public sealed record SeatsCheckoutPaidIntegrationEvent : IntegrationEvent
{
    public required Guid SeatPurchaseIntentId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required long AmountPaidCents { get; init; }
    public required string Currency { get; init; }
    public required DateTime PaidAtUtc { get; init; }
    public required string ProviderPaymentReference { get; init; }
}
