namespace BuildingBlocks.Messaging.TenantIntegrationEvents;

/// <summary>
/// La oficina cambió cuántos años guarda los registros de sus clientes (Cumplimiento, dueño: Tenant). Los
/// servicios que borran por antigüedad lo usan como retención (nunca por debajo del mínimo de su plan);
/// el borrado sigue siendo opt-in en cada uno. Null = la oficina no lo fijó (cada servicio usa su default).
/// Ordenado por <see cref="UpdatedAtUtc"/>: un consumidor ignora uno más viejo que el aplicado.
/// </summary>
public sealed record TenantRecordRetentionChangedIntegrationEvent : IntegrationEvent
{
    public required Guid OfficeTenantId { get; init; }
    public int? RetentionYears { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}
