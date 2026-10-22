using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.ConnectorsIntegrationEvents;

/// <summary>
/// El servidor IMAP rechazó el usuario o la contraseña de la cuenta. El listener no escribe en la base:
/// Connectors API marca la cuenta y avisa que hay que reconectar.
/// </summary>
[MessageIdentity("connectors.mailbox_auth_failed.v1")]
public sealed record ConnectorsMailboxAuthFailedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required DateTime FailedAtUtc { get; init; }
}
