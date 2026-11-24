using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Avisa al responsable de que ya terminó todo lo que su tarea esperaba. Sin responsable no hay a quién.
/// </summary>
public static class TaskUnblockedConsumer
{
    public static async Task Handle(
        TaskUnblockedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.AssigneeUserId is not { } assignee || assignee == Guid.Empty)
                return;

            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.TasksTaskUnblocked,
                    evt.TenantId,
                    new NotificationTarget(assignee),
                    evt.EventId,
                    "tasks.task_unblocked.v1",
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        Params: new Dictionary<string, string> { ["taskTitle"] = evt.Title },
                        ResourceId: evt.TaskId.ToString(),
                        FallbackTitle: $"Task ready to start: {evt.Title}",
                        FallbackBody: "Everything it was waiting on is done."
                    )
                ),
                ct
            );
        }
    }
}
