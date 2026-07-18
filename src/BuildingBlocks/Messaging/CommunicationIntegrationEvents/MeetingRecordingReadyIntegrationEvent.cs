using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Publicado por Communication (Node.js) cuando una grabación de meeting terminó de
/// procesarse y el archivo ya está disponible en CloudStorage. Ver docblock de
/// <see cref="MeetingInvitationCreatedIntegrationEvent"/> para la nota de compatibilidad
/// Node→.NET (TenantId/OccurredOn/CorrelationId heredados pueden llegar con sus defaults
/// hasta que se verifique end-to-end).
/// </summary>
[MessageIdentity("communication.meeting.recording_ready.v1")]
public sealed record MeetingRecordingReadyIntegrationEvent : IntegrationEvent
{
    public required Guid MeetingId { get; init; }
    public required Guid RecordingFileId { get; init; }
    public required double DurationSeconds { get; init; }
    public required int ParticipantCount { get; init; }
    public required DateTime ReadyAtUtc { get; init; }
}
