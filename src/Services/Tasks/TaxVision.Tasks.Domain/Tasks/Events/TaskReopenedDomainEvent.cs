using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks.Events;

/// <summary>
/// La tarea volvió de un terminal y vuelve a bloquear a sus sucesoras: el contador de esas tareas
/// tiene que subir otra vez. <paramref name="ReopenCount"/> es el ciclo de vida que abre, para la clave
/// del recordatorio.
/// </summary>
public sealed record TaskReopenedDomainEvent(
    Guid TaskId,
    Guid TenantId,
    Guid? ParentTaskId,
    TaskItemStatus ResumedStatus,
    Guid ReopenedByUserId,
    DateTime ReopenedAtUtc,
    int ReopenCount
) : IDomainEvent;
