using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Notifies the assigned user. Si el asignado es el propio actor, se descarta (auto-asignación).
/// </summary>
public static class TaskAssignedConsumer
{
    public static async Task Handle(
        TaskAssignedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.AssigneeUserId == Guid.Empty)
                return;
            // Un empleado que se auto-asigna una tarea no se avisa a sí mismo.
            if (evt.AssignedByUserId is { } actor && actor == evt.AssigneeUserId)
                return;

            var title = $"You were assigned a task: {evt.Title}";
            var body = evt.DueAtUtc is { } due ? $"Due {due:yyyy-MM-dd}." : null;

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.AssigneeUserId:N}",
                title,
                NotificationCategory.Collaboration,
                "tasks.task_assigned",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.AssigneeUserId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.TasksTaskAssigned,
                    evt.AssigneeUserId,
                    evt.EventId,
                    "tasks.task_assigned.v1",
                    evt.OccurredOn,
                    title,
                    FallbackBody: body,
                    ResourceId: evt.TaskId.ToString(),
                    Params: new Dictionary<string, string> { ["taskTitle"] = evt.Title }
                ),
                ct: ct
            );
        }
    }
}
