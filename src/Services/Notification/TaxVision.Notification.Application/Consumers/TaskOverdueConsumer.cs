using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Notification.Application.Common;
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

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                title,
                title,
                NotificationCategory.Collaboration,
                "task.overdue",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: assignee,
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
