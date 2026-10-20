using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// <c>task.overdue.v1</c> — una tarea pasó su vencimiento y sigue abierta.
///
/// <para>
/// Va al asignado y a nadie más. Es su trabajo el que se pasó de fecha; mandarlo a todo el que tenga
/// <c>tasks.read</c> convierte el aviso en ruido de oficina y acaba silenciado.
/// </para>
///
/// <para>
/// Sin asignado no hay a quién avisarle: la tarea aparece igual en los listados del equipo, así que
/// perder este aviso no pierde el trabajo.
/// </para>
/// </summary>
public static class TaskOverdueConsumer
{
    public static async Task Handle(
        TaskOverdueIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (evt.AssigneeUserId is not { } assignee)
            return;

        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var title = evt.IsStatutory ? $"Venció una fecha legal: {evt.Title}" : $"Tarea vencida: {evt.Title}";

            var resourceContext = new Dictionary<string, string>();
            if (evt.CustomerId is { } customerId)
                resourceContext["customerId"] = customerId.ToString();

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{assignee:N}",
                title,
                NotificationCategory.Collaboration,
                "task.overdue",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: assignee,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.TasksTaskOverdue,
                    assignee,
                    evt.EventId,
                    "tasks.task_overdue.v1",
                    evt.OccurredOn,
                    title,
                    FallbackBody: $"Vencía el {evt.DueAtUtc:yyyy-MM-dd} y sigue abierta.",
                    ResourceId: evt.TaskId.ToString(),
                    ResourceContext: resourceContext,
                    Params: new Dictionary<string, string>
                    {
                        ["taskTitle"] = evt.Title,
                        ["dueAtUtc"] = evt.DueAtUtc.ToString("O"),
                    }
                ),
                ct: ct
            );

            await dispatcher.SendPushAsync(
                evt.TenantId,
                assignee,
                title,
                $"Vencía el {evt.DueAtUtc:yyyy-MM-dd} y sigue abierta.",
                NotificationCategory.Collaboration,
                "task.overdue",
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}
