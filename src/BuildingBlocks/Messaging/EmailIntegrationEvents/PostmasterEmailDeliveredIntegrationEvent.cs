using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.EmailIntegrationEvents;

/// <summary>
/// El proveedor confirmó que el servidor del destinatario aceptó el correo. Llega después de
/// <see cref="PostmasterEmailDeliverySucceededIntegrationEvent"/> (que solo dice "el relay lo aceptó").
/// Se publica una vez, cuando el estado cambia.
/// </summary>
[MessageIdentity("postmaster.email_delivery.delivered.v1")]
public sealed record PostmasterEmailDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationLogId { get; init; }
    public required Guid DispatchAttemptId { get; init; }
    public required Guid SentMessageId { get; init; }
    public Guid? CampaignId { get; init; }
    public required DateTime EventAtUtc { get; init; }
}
