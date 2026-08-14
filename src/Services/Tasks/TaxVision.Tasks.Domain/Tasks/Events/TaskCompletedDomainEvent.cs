using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>
/// Baja el contador de bloqueadores de las sucesoras, el de subtareas del padre y materializa la
/// siguiente ocurrencia de la serie. Lleva <paramref name="ParentTaskId"/> y
/// <paramref name="SeriesId"/> para que ningún consumidor tenga que recargar la tarea.
/// Se emite una sola vez: el segundo <c>Complete()</c> es idempotente.
/// </summary>
public sealed record TaskCompletedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid? ParentTaskId,
    Guid? SeriesId,
    Guid CompletedByUserId,
    DateTime CompletedAtUtc
) : IDomainEvent;
