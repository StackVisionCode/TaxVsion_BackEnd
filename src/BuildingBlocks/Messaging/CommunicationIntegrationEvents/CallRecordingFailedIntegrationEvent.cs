using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Publicado por Communication (Node.js) cuando el pipeline de grabación de una llamada
/// 1:1 falla de forma terminal. Ver docblock de
/// <see cref="MeetingInvitationCreatedIntegrationEvent"/> para la nota de compatibilidad
/// Node→.NET.
/// </summary>
[MessageIdentity("communication.call.recording_failed.v1")]
public sealed record CallRecordingFailedIntegrationEvent : IntegrationEvent
{
    public required Guid CallId { get; init; }
    public required string Reason { get; init; }
    public required DateTime FailedAtUtc { get; init; }
}
