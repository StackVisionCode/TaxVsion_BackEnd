using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Communication (Node.js) movió la fecha de una reunión. Notification manda hora vieja y nueva con un
/// <c>.ics</c> actualizado (<c>SEQUENCE</c> mayor) a los externos sin cuenta y el in-app a los de cuenta.
/// </summary>
[MessageIdentity("communication.meeting.rescheduled.v1")]
public sealed record MeetingRescheduledIntegrationEvent : IntegrationEvent
{
    public required Guid MeetingId { get; init; }
    public required Guid RescheduledByUserId { get; init; }
    public DateTime? PreviousScheduledForUtc { get; init; }
    public DateTime? NewScheduledForUtc { get; init; }
    public required DateTime RescheduledAtUtc { get; init; }
    public IReadOnlyList<Guid> ParticipantUserIds { get; init; } = [];

    /// <summary>Solo invitados externos sin cuenta.</summary>
    public IReadOnlyList<string> InvitedEmails { get; init; } = [];

    public string? MeetingTitle { get; init; }
    public string? ShortCode { get; init; }
    public int? DurationMinutes { get; init; }
    public Guid? HostUserId { get; init; }
    public string? HostDisplayName { get; init; }
    public string? TimeZone { get; init; }
    public IReadOnlyList<Guid> InviteeUserIds { get; init; } = [];
}
