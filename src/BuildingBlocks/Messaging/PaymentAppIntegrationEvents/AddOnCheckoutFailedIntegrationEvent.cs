namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// El pago por HOSTED-CHECKOUT de la compra de un add-on falló o se canceló. Subscription lo consume para
/// cerrar la <c>AddOnPurchaseIntent</c>: el add-on nunca llegó a existir, así que no hay nada que revertir.
/// Alias v1.
/// </summary>
public sealed record AddOnCheckoutFailedIntegrationEvent : IntegrationEvent
{
    public required Guid AddOnPurchaseIntentId { get; init; }
    public required Guid SaaSPaymentId { get; init; }
    public required string FailureCode { get; init; }
    public required string FailureReason { get; init; }
}
