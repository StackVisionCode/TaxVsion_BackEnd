namespace BuildingBlocks.Messaging.PaymentAppIntegrationEvents;

/// <summary>
/// Publicado por PaymentApp cuando un método de pago guardado vence dentro de los próximos
/// 30 días. Notification (u otro consumer futuro) lo consume para avisar al tenant antes de
/// que una renovación automática falle por tarjeta vencida.
/// </summary>
public sealed record SaaSPaymentMethodExpiringSoonIntegrationEvent : IntegrationEvent
{
    public required Guid TenantProviderCustomerId { get; init; }
    public required Guid PaymentMethodId { get; init; }
    public required string Brand { get; init; }
    public required string Last4 { get; init; }
    public required int ExpMonth { get; init; }
    public required int ExpYear { get; init; }
    public required bool IsDefault { get; init; }
}
