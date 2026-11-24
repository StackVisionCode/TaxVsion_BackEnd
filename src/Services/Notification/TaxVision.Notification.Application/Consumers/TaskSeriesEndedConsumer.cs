using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>Avisa a quien gestiona una tarea recurrente de que terminó sola y ya no creará más tareas.</summary>
public static class TaskSeriesEndedConsumer
{
    public static async Task Handle(
        TaskSeriesEndedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.OwnerUserId == Guid.Empty)
                return;

            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.TasksSeriesEnded,
                    evt.TenantId,
                    new NotificationTarget(evt.OwnerUserId),
                    evt.EventId,
                    "tasks.series_ended.v1",
                    evt.OccurredOn,
                    correlation.CorrelationId,
                    InApp: new InAppContent(
                        Params: new Dictionary<string, string> { ["taskTitle"] = evt.Title },
                        ResourceId: evt.SeriesId.ToString(),
                        FallbackTitle: $"Recurring task ended: {evt.Title}",
                        FallbackBody: "No more tasks will be created for it."
                    )
                ),
                ct
            );
        }
    }
}
