using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Publicado por Communication (Node.js) cuando una grabación de llamada 1:1 terminó de
/// procesarse y el archivo ya está disponible en CloudStorage. Ver docblock de
/// <see cref="MeetingInvitationCreatedIntegrationEvent"/> para la nota de compatibilidad
/// Node→.NET.
/// </summary>
[MessageIdentity("communication.call.recording_ready.v1")]
public sealed record CallRecordingReadyIntegrationEvent : IntegrationEvent
{
    public required Guid CallId { get; init; }
    public required Guid RecordingFileId { get; init; }
    public required double DurationSeconds { get; init; }
    public required DateTime ReadyAtUtc { get; init; }

    /// <summary>Fase 1B — ambas partes, no un unico "actor": una Call no tiene dueño unico.</summary>
    public required Guid CallerUserId { get; init; }
    public required Guid CalleeUserId { get; init; }
}
