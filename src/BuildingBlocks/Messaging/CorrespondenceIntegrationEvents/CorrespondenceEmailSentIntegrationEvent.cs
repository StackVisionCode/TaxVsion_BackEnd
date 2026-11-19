using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;

/// <summary>
/// Un miembro del staff envió un correo desde Correspondence (Work Profile). Solo ids, sin asunto ni
/// contenido: quien lo muestre resuelve el hilo con sus propios permisos.
/// </summary>
[MessageIdentity("correspondence.email.sent.v1")]
public sealed record CorrespondenceEmailSentIntegrationEvent : IntegrationEvent
{
    public required Guid DraftId { get; init; }

    // null = correo nuevo: la conversación nace cuando llega algo sobre él.
    public Guid? ThreadId { get; init; }

    // null = conversación con alguien que no es cliente.
    public Guid? CustomerId { get; init; }
    public required Guid SentByUserId { get; init; }
}
