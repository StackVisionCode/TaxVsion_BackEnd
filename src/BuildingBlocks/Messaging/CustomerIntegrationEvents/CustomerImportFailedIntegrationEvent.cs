namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

/// <summary>
/// Fase 1B — publicado cuando un job de bulk import termina en Failed (archivo vacio, excede el
/// maximo de filas, virus detectado, bloqueado por politica de contenido, o crash del worker).
/// Evento hermano de CustomersBulkImportedIntegrationEvent (que solo cubre Completed/Canceled) —
/// antes de esta fase no se publicaba nada en el camino de falla, y el empleado que inicio el
/// import nunca se enteraba salvo que volviera a consultar el estado a mano.
///
/// NO incluye PII: Reason es el mismo string ya vetted por CustomerImportAttempt.Fail (sin PII,
/// truncado a 500 chars).
/// </summary>
public sealed record CustomerImportFailedIntegrationEvent : IntegrationEvent
{
    public required Guid ImportJobId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required string Reason { get; init; }
    public required DateTime FailedAtUtc { get; init; }
}
