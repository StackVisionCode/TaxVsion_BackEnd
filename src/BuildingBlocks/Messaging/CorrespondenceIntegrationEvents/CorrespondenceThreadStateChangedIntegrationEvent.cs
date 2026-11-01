using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;

/// <summary>
/// Cambió el estado visible de un hilo (leído/no leído, archivado, mensajes en la papelera). Solo ids y
/// contadores, sin asunto ni contenido: el CRM lo aplica en vivo y pide el resto por HTTP.
/// <see cref="ActorUserId"/> = quien hizo el cambio (null si no se conoce).
/// </summary>
[MessageIdentity("correspondence.thread_state_changed.v1")]
public sealed record CorrespondenceThreadStateChangedIntegrationEvent : IntegrationEvent
{
    public required Guid EmailThreadId { get; init; }
    public required Guid CustomerId { get; init; }
    public required string Status { get; init; }
    public required int UnreadCount { get; init; }
    public required int MessageCount { get; init; }
    public Guid? ActorUserId { get; init; }
}
