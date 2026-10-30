using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Recordatorio de reunión agendada (barrido del scheduler de Communication, F6 §1.4): una sola vez por
/// reunión, <see cref="MinutesBefore"/> antes de la hora. Notification manda correo a los externos sin
/// cuenta e in-app a participantes e invitados con cuenta.
/// </summary>
[MessageIdentity("communication.meeting.reminder.v1")]
public sealed record MeetingReminderIntegrationEvent : IntegrationEvent
{
    public required Guid MeetingId { get; init; }
    public string? MeetingTitle { get; init; }
    public string? ShortCode { get; init; }
    public required DateTime ScheduledForUtc { get; init; }
    public int? DurationMinutes { get; init; }
    public required int MinutesBefore { get; init; }
    public Guid? HostUserId { get; init; }
    public string? HostDisplayName { get; init; }
    public string? TimeZone { get; init; }
    public IReadOnlyList<Guid> ParticipantUserIds { get; init; } = [];
    public IReadOnlyList<Guid> InviteeUserIds { get; init; } = [];

    /// <summary>Solo invitados externos sin cuenta.</summary>
    public IReadOnlyList<string> InvitedEmails { get; init; } = [];
    public string? StaffJoinUrl { get; init; }
    public string? PortalJoinUrl { get; init; }
}
