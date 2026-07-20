namespace BuildingBlocks.Messaging.GrowthIntegrationEvents;

/// <summary>
/// Envelope versionado para hechos de integración de Growth. Extiende el contrato
/// existente sin cambiar eventos publicados por otros servicios.
/// </summary>
public abstract record GrowthIntegrationEvent : IntegrationEvent
{
    public abstract string EventType { get; }
    public int EventVersion { get; init; } = 1;
    public DateTime OccurredAt => OccurredOn;
    public string CausationId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public long AggregateVersion { get; init; }
}
