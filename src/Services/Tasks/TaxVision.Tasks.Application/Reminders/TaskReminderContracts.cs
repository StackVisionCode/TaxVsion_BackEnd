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

    /// <summary>Categoría del aviso "el cliente no respondió": se mueve y se cierra aparte del vencimiento.</summary>
    public const string ClientResponseCategory = "TaskClientResponse";

    /// <summary>
    /// Reminder ignora una clave existente en cualquier estado (REM-1), así que cada reapertura usa
    /// la suya. El ciclo 0 conserva el formato original para no cambiar las claves vivas.
    /// </summary>
    public static string RequestKey(Guid taskId, Guid userId, int lifecycle, string? cycleTag = null) =>
        (lifecycle == 0 ? $"task-created:{taskId:N}:{userId:N}" : $"task-created:{taskId:N}:{userId:N}:r{lifecycle}")
        + (cycleTag is null ? "" : $":{cycleTag}");

    /// <param name="lifecycle">Ciclo de vida de la clave. Sin valor, el vigente de la tarea.</param>
    public static ReminderRequestedIntegrationEvent Requested(
        TaskItem task,
        Guid userId,
        int leadMinutes,
        string correlationId,
        int? lifecycle = null,
        string? cycleTag = null
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
            RequestKey = RequestKey(task.Id, userId, lifecycle ?? task.ReopenCount, cycleTag),
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
        public const string Deleted = "deleted";
        public const string LeftWaiting = "left_waiting_on_client";
    }

    /// <summary>La espera terminó: se cierra su aviso, no el del vencimiento.</summary>
    public static ReminderTargetClosedIntegrationEvent ClientResponseClosed(
        Guid tenantId,
        Guid taskId,
        string reason,
        string correlationId
    ) => Closed(tenantId, taskId, reason, correlationId) with { Category = ClientResponseCategory };

    public static ReminderTargetClosedIntegrationEvent Closed(TaskItem task, string reason, string correlationId) =>
        Closed(task.TenantId, task.Id, reason, correlationId);

    public static ReminderTargetClosedIntegrationEvent Closed(
        Guid tenantId,
        Guid taskId,
        string reason,
        string correlationId
    ) =>
        new()
        {
            TenantId = tenantId,
            CorrelationId = correlationId,
            Category = Category,
            TargetId = taskId,
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
        string officeTimeZoneId,
        string correlationId
    ) =>
        new()
        {
            TenantId = task.TenantId,
            CorrelationId = correlationId,
            UserId = preparerUserId,
            Category = ClientResponseCategory,
            TargetId = task.Id,
            Title = $"No reply from the client: {task.Title.Value}",
            Body = task.ExpectedItems?.Value,
            TimeZoneId = task.Due?.TimeZoneId ?? officeTimeZoneId,
            AnchorAtUtc = clientDueAtUtc,
            LeadMinutes = 0,
            // Una clave por espera: la segunda no choca con el recordatorio ya disparado de la primera.
            RequestKey = $"task-client-response:{task.Id:N}:{preparerUserId:N}:{task.ClientRequestedAtUtc?.Ticks ?? 0}",
        };
}
