using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Publicado por Communication (Node.js) cuando el pipeline de grabación de un meeting
/// falla de forma terminal (validación, transcodeo, o error del transcript worker). Ver
/// docblock de <see cref="MeetingInvitationCreatedIntegrationEvent"/> para la nota de
/// compatibilidad Node→.NET.
/// </summary>
[MessageIdentity("communication.meeting.recording_failed.v1")]
public sealed record MeetingRecordingFailedIntegrationEvent : IntegrationEvent
{
    public required Guid MeetingId { get; init; }
    public required string Reason { get; init; }
    public required DateTime FailedAtUtc { get; init; }

    /// <summary>Fase 1B — antes ausente; Notification usaba un recipient simbolico "meeting:{id}".</summary>
    public required Guid HostUserId { get; init; }
}
