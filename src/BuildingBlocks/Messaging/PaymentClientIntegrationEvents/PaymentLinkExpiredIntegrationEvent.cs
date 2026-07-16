namespace BuildingBlocks.Messaging.PaymentClientIntegrationEvents;

/// <summary>Publicado por <c>PaymentLinkExpirationJob</c> cuando un link vencido pasa de
/// <c>Active</c> a <c>Expired</c> sin haber sido usado.</summary>
public sealed record PaymentLinkExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentLinkId { get; init; }
}
