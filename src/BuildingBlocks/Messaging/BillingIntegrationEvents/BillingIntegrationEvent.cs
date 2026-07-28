namespace BuildingBlocks.Messaging.BillingIntegrationEvents;

/// <summary>
/// Envelope versionado para hechos de integración de Billing (facturación tenant→taxpayer).
/// Mismo contrato que los demás servicios productores (p.ej. GrowthIntegrationEvent).
/// </summary>
public abstract record BillingIntegrationEvent : IntegrationEvent
{
    public abstract string EventType { get; }
    public int EventVersion { get; init; } = 1;
    public DateTime OccurredAt => OccurredOn;
    public string CausationId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public long AggregateVersion { get; init; }
}
