namespace TaxVision.Reminder.Application.Reminders.Abstractions;

/// <summary>
/// Puerto del scheduler. El Domain no conoce Quartz — emite eventos; Application habla por este
/// puerto; el adaptador (<c>QuartzReminderScheduler</c>) vive en Infrastructure.
///
/// <para>
/// <b>EF y Quartz no comparten transacción.</b> El orden elegido es EF primero, Quartz después: el
/// riesgo aceptado es un recordatorio guardado sin trigger, nunca un trigger huérfano apuntando a
/// una fila que no existe. Ese riesgo lo cubre <c>ReminderScheduleReconciliationJob</c>.
/// </para>
///
/// <para>
/// Las tres operaciones son <b>idempotentes</b>: agendar dos veces reemplaza el trigger,
/// desagendar algo que no existe es un no-op. Sin eso, la reconciliación no podría reintentar sin
/// miedo.
/// </para>
/// </summary>
public interface IReminderScheduler
{
    Task ScheduleAsync(Guid tenantId, Guid reminderId, DateTime fireAtUtc, CancellationToken ct = default);

    Task RescheduleAsync(Guid tenantId, Guid reminderId, DateTime newFireAtUtc, CancellationToken ct = default);

    Task UnscheduleAsync(Guid tenantId, Guid reminderId, CancellationToken ct = default);

    /// <summary>
    /// ¿Hay un trigger vivo para este recordatorio? Lo usa la reconciliación para no reagendar lo
    /// que ya está bien. Es la única lectura del puerto — el resto son órdenes.
    /// </summary>
    Task<bool> IsScheduledAsync(Guid tenantId, Guid reminderId, CancellationToken ct = default);
}
