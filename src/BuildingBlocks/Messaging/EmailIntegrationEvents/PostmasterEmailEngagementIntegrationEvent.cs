using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.EmailIntegrationEvents;

/// <summary>
/// Primera apertura, primer clic o baja del destinatario. Aproximado: algunos clientes de correo abren
/// los mensajes solos. Se publica solo la primera vez de cada señal.
/// </summary>
[MessageIdentity("postmaster.email_engagement.v1")]
public sealed record PostmasterEmailEngagementIntegrationEvent : IntegrationEvent
{
    public required Guid NotificationLogId { get; init; }
    public required Guid DispatchAttemptId { get; init; }
    public required Guid SentMessageId { get; init; }

    /// <summary>"Opened", "Clicked" o "Unsubscribed".</summary>
    public required string Signal { get; init; }

    public Guid? CampaignId { get; init; }
    public required DateTime EventAtUtc { get; init; }
}
