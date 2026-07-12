namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// La solicitud pasó a InProgress: los tokens públicos se emitieron y las invitaciones
/// están listas para dispatch. Notification lo consume para enviar los correos/SMS a
/// los firmantes en el canal preseleccionado.
/// </summary>
public sealed record SignatureRequestSentIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required DateTime SentAtUtc { get; init; }
    public required IReadOnlyList<Guid> SignerIds { get; init; }
}
