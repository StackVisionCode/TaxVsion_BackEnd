using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Communication (Node.js) canceló una reunión. Notification manda el correo con el <c>.ics</c> de
/// cancelación a los externos sin cuenta (<see cref="InvitedEmails"/>) y el in-app a participantes e
/// invitados con cuenta. Mismas notas de deserialización Node→.NET que
/// <see cref="MeetingInvitationCreatedIntegrationEvent"/>.
/// </summary>
[MessageIdentity("communication.meeting.cancelled.v1")]
public sealed record MeetingCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid MeetingId { get; init; }
    public required Guid CancelledByUserId { get; init; }
    public required DateTime CancelledAtUtc { get; init; }

    /// <summary>Participantes ya registrados (Host + Cohost + Attendee).</summary>
    public IReadOnlyList<Guid> ParticipantUserIds { get; init; } = [];

    /// <summary>Solo invitados externos sin cuenta: los de cuenta van por in-app.</summary>
    public IReadOnlyList<string> InvitedEmails { get; init; } = [];
    public string? Reason { get; init; }

    public string? MeetingTitle { get; init; }
    public string? ShortCode { get; init; }

    /// <summary>La fecha que tenía la reunión al cancelarse (null = nunca se agendó).</summary>
    public DateTime? ScheduledForUtc { get; init; }
    public int? DurationMinutes { get; init; }
    public Guid? HostUserId { get; init; }
    public string? HostDisplayName { get; init; }
    public string? TimeZone { get; init; }

    /// <summary>Invitados con cuenta y sin revocar.</summary>
    public IReadOnlyList<Guid> InviteeUserIds { get; init; } = [];
}
