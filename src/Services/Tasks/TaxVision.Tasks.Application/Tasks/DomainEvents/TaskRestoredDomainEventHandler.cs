using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Tasks.Application.Reminders;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks.Events;

namespace TaxVision.Tasks.Application.Tasks.DomainEvents;

/// <summary>Volvió de la papelera: el recordatorio de vencimiento se pide con una clave nueva.</summary>
public static class TaskRestoredDomainEventHandler
{
    public static async Task<ReminderRequestedIntegrationEvent?> Handle(
        TaskRestoredDomainEvent restored,
        ITaskRepository tasks,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdAsync(restored.TenantId, restored.TaskId, ct);
        if (found.IsFailure)
            return null;
        var task = found.Value;
        return TaskDueReminder.RequestFor(
            task,
            task.AssigneeUserId,
            task.ReopenCount,
            correlation.CorrelationId,
            cycleTag: $"t{restored.RestoredAtUtc.Ticks}"
        );
    }
}
