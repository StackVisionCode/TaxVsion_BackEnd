using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.EmailIntegrationEvents;

/// <summary>
/// El destinatario marcó el correo como spam. Postmaster ya suprimió la dirección; esto es para que
/// Notification lo muestre.
/// </summary>
[MessageIdentity("postmaster.email_delivery.complained.v1")]
public sealed record PostmasterEmailComplainedIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationLogId { get; init; }
    public required Guid DispatchAttemptId { get; init; }
    public required Guid SentMessageId { get; init; }
    public Guid? CampaignId { get; init; }
    public required DateTime EventAtUtc { get; init; }
}
