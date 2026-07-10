namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// El firmante completó su firma. Se emite un evento por firmante — no uno por campo.
/// Notification lo consume para avisar al staff (dashboard realtime) y a los demás
/// firmantes si la firma es secuencial. Planner marca la tarea de seguimiento como
/// completada.
/// </summary>
public sealed record DocumentSignedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid SignerId { get; init; }
    public required DateTime SignedAtUtc { get; init; }
    public required int TotalSignersCount { get; init; }
    public required int SignedSignersCount { get; init; }
    public required bool IsRequestCompleted { get; init; }
    public string? ClientIp { get; init; }
}
