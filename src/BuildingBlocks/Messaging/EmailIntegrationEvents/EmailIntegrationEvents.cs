namespace BuildingBlocks.Messaging.EmailIntegrationEvents;

/// <summary>
/// Solicitud de entrega de un correo saliente ya persistido (estado Queued). La publica el handler de
/// envío y la consume el propio servicio Notification para entregar de forma asíncrona y durable
/// (fuera del request HTTP). El cuerpo NO viaja en el evento: se lee de la BD por MessageId.
/// </summary>
public sealed record EmailSendRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid MessageId { get; init; }
}

/// <summary>El correo se entregó al proveedor correctamente.</summary>
public sealed record EmailDeliverySucceededIntegrationEvent : IntegrationEvent
{
    public required Guid MessageId { get; init; }
    public required string ProviderType { get; init; }
    public Guid? CampaignId { get; init; }
}

/// <summary>El correo falló al entregarse (tras agotar reintentos o error no recuperable).</summary>
public sealed record EmailDeliveryFailedIntegrationEvent : IntegrationEvent
{
    public required Guid MessageId { get; init; }
    public required string Error { get; init; }
    public Guid? CampaignId { get; init; }
}
