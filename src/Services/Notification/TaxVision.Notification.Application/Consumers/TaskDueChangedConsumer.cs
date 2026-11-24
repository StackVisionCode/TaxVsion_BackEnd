using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Otra persona cambió el vencimiento de una tarea: su responsable se entera. Si lo cambió el propio responsable, o
/// la tarea no tiene responsable, no hay a quién avisar.
/// </summary>
public static class TaskDueChangedConsumer
{
    public static async Task Handle(
        TaskDueChangedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.AssigneeUserId is not { } assignee || assignee == Guid.Empty || assignee == evt.ChangedByUserId)
                return;

            var title = evt.Title ?? "a task";
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.TasksTaskDueChanged,
                    evt.TenantId,
                    new NotificationTarget(assignee),
                    evt.EventId,
                    "tasks.task_due_changed.v1",
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        Params: new Dictionary<string, string> { ["taskTitle"] = title },
                        ResourceId: evt.TaskId.ToString(),
                        FallbackTitle: $"Due date changed: {title}",
                        FallbackBody: BodyOf(evt.NewDueOn)
                    )
                ),
                ct
            );
        }
    }

    private static string BodyOf(DateOnly? dueOn) =>
        dueOn is { } date
            ? $"New due date: {date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}."
            : "The due date was removed.";
}
