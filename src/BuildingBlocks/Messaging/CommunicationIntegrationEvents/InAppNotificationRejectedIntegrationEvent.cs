using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Communication descartó una petición in-app por inválida (no mejora al reintentar). Notification marca su fila
/// como fallida. No lleva el contenido rechazado: <see cref="Detail"/> son las rutas de los campos, sin valores.
/// </summary>
[MessageIdentity("communication.notification.inapp_rejected.v1")]
public sealed record InAppNotificationRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid SourceEventId { get; init; }
    public required Guid RecipientUserId { get; init; }

    // Puede venir vacío si el kind era justo lo inválido.
    public string Kind { get; init; } = string.Empty;
    public required string Reason { get; init; }
    public string? Detail { get; init; }
}
