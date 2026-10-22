using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.ConnectorsIntegrationEvents;

/// <summary>
/// El listener de buzones IMAP vio que cambió la bandeja (llegó correo). Solo avisa: la descarga la
/// hace Connectors API con la reconciliación de siempre.
/// </summary>
[MessageIdentity("connectors.mailbox_change_detected.v1")]
public sealed record ConnectorsMailboxChangeDetectedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required DateTime DetectedAtUtc { get; init; }
}
