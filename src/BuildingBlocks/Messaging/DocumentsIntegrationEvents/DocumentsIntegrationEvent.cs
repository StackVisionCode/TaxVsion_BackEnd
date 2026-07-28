namespace BuildingBlocks.Messaging.DocumentsIntegrationEvents;

/// <summary>Envelope versionado para hechos de integración de Documents. Mismo contrato que los
/// demás servicios productores (p.ej. GrowthIntegrationEvent). Los eventos transportan REFERENCIAS
/// (FileId), nunca bytes/Base64.</summary>
public abstract record DocumentsIntegrationEvent : IntegrationEvent
{
    public abstract string EventType { get; }
    public int EventVersion { get; init; } = 1;
    public DateTime OccurredAt => OccurredOn;
    public string CausationId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public long AggregateVersion { get; init; }
}
