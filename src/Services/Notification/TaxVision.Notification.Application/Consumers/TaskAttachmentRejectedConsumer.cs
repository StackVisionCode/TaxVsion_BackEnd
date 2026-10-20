using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// <c>task.attachment_rejected.v1</c> — el escaneo rechazó un archivo que alguien ya había adjuntado.
///
/// <para>
/// El aviso llega a menudo <b>después</b> de que la tarea se cerró: adjuntar no bloquea completar, y
/// para cuando ClamAV se pronuncia nadie está mirando esa tarea. Por eso va dirigido a quien
/// adjuntó, con su id, y no a la audiencia de la tarea.
/// </para>
///
/// <para>
/// In-app y push, sin correo: el destinatario es personal de la firma con sesión abierta en el
/// panel, y el evento ya trae su id de usuario —no hace falta resolver una dirección—.
/// </para>
/// </summary>
public static class TaskAttachmentRejectedConsumer
{
    public static async Task Handle(
        TaskAttachmentRejectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var title = $"Archivo rechazado: {evt.DisplayName}";

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                $"user:{evt.AttachedByUserId:N}",
                title,
                NotificationCategory.DocumentsAndSignatures,
                "task.attachment_rejected",
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.AttachedByUserId,
                inApp: new InAppNotificationRequest(
                    NotificationKinds.TasksAttachmentRejected,
                    evt.AttachedByUserId,
                    evt.EventId,
                    "tasks.attachment_rejected.v1",
                    evt.OccurredOn,
                    title,
                    FallbackBody: $"El escaneo lo marcó como «{evt.Reason}» en «{evt.TaskTitle}».",
                    ResourceId: evt.TaskId.ToString(),
                    Params: new Dictionary<string, string>
                    {
                        ["displayName"] = evt.DisplayName,
                        ["taskTitle"] = evt.TaskTitle,
                    }
                ),
                ct: ct
            );

            await dispatcher.SendPushAsync(
                evt.TenantId,
                evt.AttachedByUserId,
                title,
                $"El escaneo lo marcó como «{evt.Reason}» en la tarea «{evt.TaskTitle}». Habrá que reemplazarlo.",
                NotificationCategory.DocumentsAndSignatures,
                "task.attachment_rejected",
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}
