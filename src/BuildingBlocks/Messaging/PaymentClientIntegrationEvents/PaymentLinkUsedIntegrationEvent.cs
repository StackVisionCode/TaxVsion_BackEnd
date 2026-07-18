namespace BuildingBlocks.Messaging.PaymentClientIntegrationEvents;

/// <summary>Publicado cuando un taxpayer redime exitosamente un link mágico — el tenant lo
/// consume para reconciliar contra la invoice/caso que originó el cobro.</summary>
public sealed record PaymentLinkUsedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentLinkId { get; init; }
    public required Guid TenantPaymentId { get; init; }
    public required long AmountCents { get; init; }
    public required string Currency { get; init; }
    public required DateTime UsedAtUtc { get; init; }
}
