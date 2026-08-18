using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>
/// La tarea volvió de un terminal y vuelve a bloquear a sus sucesoras: el contador de esas tareas
/// tiene que subir otra vez.
/// </summary>
public sealed record TaskReopenedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid? ParentTaskId,
    TaskItemStatus ResumedStatus,
    Guid ReopenedByUserId,
    DateTime ReopenedAtUtc
) : IDomainEvent;
