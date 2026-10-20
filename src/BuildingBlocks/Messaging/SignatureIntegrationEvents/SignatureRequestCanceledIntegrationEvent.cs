namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El staff canceló la solicitud antes de completarse. Los tokens públicos vigentes
/// quedan revocados (RevocationEpoch incrementado). Notification, Planner y el
/// dashboard deben reaccionar cerrando tareas y notificando a los firmantes pendientes.
/// </summary>
public sealed record SignatureRequestCanceledIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid CanceledByUserId { get; init; }
    /// <summary>
    /// Snapshot del nombre del usuario que canceló, tomado del claim del access
    /// token al momento del request. Permite al consumer de Communication rellenar
    /// `actorDisplayName` de la fila de la campana sin tener que resolver el
    /// UserId contra directory. Nullable para publishers viejos que no lo mandan.
    /// </summary>
    public string? CanceledByUserDisplayName { get; init; }
    public required DateTime CanceledAtUtc { get; init; }
    public string? Reason { get; init; }
    public required IReadOnlyList<Guid> PendingSignerIds { get; init; }
}
