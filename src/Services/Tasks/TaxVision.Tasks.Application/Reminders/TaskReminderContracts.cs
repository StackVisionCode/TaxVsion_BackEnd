using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Reminders;

/// <summary>
/// Task no conoce a Reminder: publica los contratos que Reminder define. La dirección importa —
/// invertirla obligaría a Reminder a aprender el vocabulario de Task y lo dejaría sin poder atender a
/// un cuarto servicio sin volver a tocarse.
/// </summary>
public static class TaskReminderContracts
{
    /// <summary>Lo que Reminder usa para deduplicar reintentos del bus.</summary>
    public const string Category = "Task";

    public static string RequestKey(Guid taskId, Guid userId) => $"task-created:{taskId:N}:{userId:N}";

    public static ReminderRequestedIntegrationEvent Requested(
        TaskItem task,
        Guid userId,
        int leadMinutes,
        string correlationId
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            UserId = userId,
            Category = Category,
            TargetId = task.Id,
            Title = task.Title.Value,
            Body = task.Description?.Value,
            TimeZoneId = task.Due!.TimeZoneId,
            AnchorAtUtc = task.Due.DueAtUtc,
            LeadMinutes = leadMinutes,
            RequestKey = RequestKey(task.Id, userId),
        };

    public static ReminderTargetMovedIntegrationEvent Moved(TaskItem task, string correlationId) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            Category = Category,
            TargetId = task.Id,
            NewAnchorAtUtc = task.Due!.DueAtUtc,
        };

    /// <summary>
    /// El que se olvida. Sin él, el recordatorio de una tarea ya hecha llega igual el viernes a las
    /// 8 AM y nadie encuentra por qué.
    /// </summary>
    /// <summary>Los dos motivos por los que Reminder deja de esperar a una tarea.</summary>
    public static class ClosureReasons
    {
        public const string Completed = "completed";
        public const string Cancelled = "cancelled";
    }

    public static ReminderTargetClosedIntegrationEvent Closed(TaskItem task, string reason, string correlationId) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            Category = Category,
            TargetId = task.Id,
            Reason = reason,
        };

    /// <summary>
    /// «Le pediste algo al cliente y todavía no llegó». Va al preparador asignado, con la fecha que
    /// se le dio al cliente como ancla. <c>RequestKey</c> propio para que no colisione con el
    /// recordatorio del vencimiento de la misma tarea: son dos avisos distintos sobre el mismo id.
    /// </summary>
    public static ReminderRequestedIntegrationEvent ClientResponseExpected(
        TaskItem task,
        Guid preparerUserId,
        DateTime clientDueAtUtc,
        string correlationId
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            UserId = preparerUserId,
            Category = Category,
            TargetId = task.Id,
            Title = $"Sin respuesta del cliente: {task.Title.Value}",
            Body = task.ExpectedItems?.Value,
            TimeZoneId = task.Due?.TimeZoneId ?? "UTC",
            AnchorAtUtc = clientDueAtUtc,
            LeadMinutes = 0,
            RequestKey = $"task-client-response:{task.Id:N}:{preparerUserId:N}",
        };
}
