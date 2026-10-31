using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// El host revocó una invitación (F6 §1.2). Solo in-app al invitado con cuenta; a un externo no se le
/// escribe (su enlace simplemente deja de funcionar). <see cref="Ejected"/> = estaba dentro y se le sacó.
/// </summary>
[MessageIdentity("communication.meeting.invitation_revoked.v1")]
public sealed record MeetingInvitationRevokedIntegrationEvent : IntegrationEvent
{
    public required Guid InvitationId { get; init; }
    public required Guid MeetingId { get; init; }
    public string? MeetingTitle { get; init; }
    public required string InviteeKind { get; init; }
    public Guid? InviteeUserId { get; init; }
    public string? InviteeEmail { get; init; }
    public required Guid RevokedByUserId { get; init; }
    public bool Ejected { get; init; }
}
