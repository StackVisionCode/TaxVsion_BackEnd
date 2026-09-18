namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// El pago por HOSTED-CHECKOUT de una compra de asientos falló o la sesión expiró/se canceló. Subscription lo
/// consume para marcar la <c>SeatPurchaseIntent</c> como fallida (no aprovisiona). Alias v1.
/// </summary>
public sealed record SeatsCheckoutFailedIntegrationEvent : IntegrationEvent
{
    public required Guid SeatPurchaseIntentId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string FailureCode { get; init; }
    public required string FailureReason { get; init; }
}
