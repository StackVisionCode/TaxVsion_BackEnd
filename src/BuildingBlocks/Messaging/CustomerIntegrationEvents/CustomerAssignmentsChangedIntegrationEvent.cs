namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

// Snapshot del set COMPLETO de staff asignado a un cliente (event-carried state transfer). Se publica en
// CADA cambio de asignación (assign/unassign/grant/revoke/bulk/alta/offboard). Los servicios downstream
// proyectan "cliente → asignados" REEMPLAZANDO el set (idempotente, sin lógica de merge) y usan Version
// para descartar eventos reordenados o duplicados (aplican solo si Version es más nueva que la última).
// Es la fuente canónica para las proyecciones de visibilidad por-cliente (P2). Los eventos Preparer*
// siguen para Communication (compat).
public sealed record CustomerAssignmentsChangedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required IReadOnlyList<Guid> AssigneeUserIds { get; init; }
    public Guid? PrimaryUserId { get; init; }

    /// <summary>Versión monotónica por-cliente (UpdatedAtUtc del agregado, que Touch() bumpea en cada
    /// cambio). El consumer aplica el snapshot solo si Version &gt; la última aplicada para ese cliente.</summary>
    public required DateTime Version { get; init; }
}
