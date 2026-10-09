namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando el cobro de una recarga de monedero falla (tarjeta rechazada, error
/// del proveedor, etc.). Wallet lo consume para marcar la orden <c>WalletTopUp</c> como Failed. No acredita.
/// </summary>
public sealed record WalletTopUpPaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid TopUpId { get; init; }
    public Guid? SaaSPaymentId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Reason { get; init; }
    public required Guid RequestedByUserId { get; init; }
}
