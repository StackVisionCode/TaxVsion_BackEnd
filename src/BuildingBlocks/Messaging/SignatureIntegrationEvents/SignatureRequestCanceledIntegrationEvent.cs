namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El staff canceló la solicitud antes de completarse. Los tokens públicos vigentes
/// quedan revocados (RevocationEpoch incrementado). Notification, Planner y el
/// dashboard deben reaccionar cerrando tareas y notificando a los firmantes pendientes.
/// </summary>
public sealed record SignatureRequestCanceledIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CanceledByUserId { get; init; }
    public required DateTime CanceledAtUtc { get; init; }
    public string? Reason { get; init; }
    public required IReadOnlyList<Guid> PendingSignerIds { get; init; }
}
