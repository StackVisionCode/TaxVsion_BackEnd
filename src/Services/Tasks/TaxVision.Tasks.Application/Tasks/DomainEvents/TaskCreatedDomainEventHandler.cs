using BuildingBlocks.Common;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks.Events;
using Wolverine;

namespace TaxVision.Tasks.Application.Tasks.DomainEvents;

/// <summary>
/// Nació una tarea (alta, subtarea, plantilla o serie): devuelve sus anuncios. Corre después del commit, desde
/// la cola domain-events; si la tarea ya no existe no hay nada que anunciar.
/// </summary>
public static class TaskCreatedDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        TaskCreatedDomainEvent born,
        ITaskRepository tasks,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdAsync(born.TenantId, born.TaskId, ct);
        return found.IsSuccess
            ? TaskBirthAnnouncements.For(found.Value, born, correlation.CorrelationId)
            : new OutgoingMessages();
    }
}
