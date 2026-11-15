using BuildingBlocks.TimeZones;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

public readonly record struct ReminderDecision(bool ShouldSend, bool IsOverdue);

/// <summary>
/// Decide si una request necesita un recordatorio automático. Función pura: sin IO ni reloj propio,
/// el <paramref name="nowUtc"/> y las opciones llegan de fuera para poder cubrirla con una tabla de casos.
/// </summary>
public static class ReminderSchedule
{
    public static ReminderDecision Evaluate(
        FormRequestStatus status,
        DateTime? dueAtUtc,
        DateTime? lastReminderAtUtc,
        int reminderCount,
        DateTime nowUtc,
        ClientFormsReminderOptions options,
        DateOnly? dueOn = null,
        ResolvedTimeZone? officeZone = null
    )
    {
        var isOverdue = dueAtUtc is { } due && nowUtc > due;
        // Solo las abiertas (Pending o Returned) reciben recordatorios.
        if (status is not (FormRequestStatus.Pending or FormRequestStatus.Returned))
            return new ReminderDecision(false, isOverdue);
        // El tope cuenta también los manuales.
        if (!options.Enabled || reminderCount >= options.MaxReminders)
            return new ReminderDecision(false, isOverdue);
        // Sin fecha límite no hay automáticos.
        if (dueAtUtc is not { } dueAt)
            return new ReminderDecision(false, false);

        // Previos: alguna etapa ya alcanzada y todavía sin recordar después de ella.
        foreach (var days in options.DaysBeforeDue)
        {
            if (days <= 0)
                continue;
            var stage = StageAt(dueAt, days, dueOn, officeZone);
            if (nowUtc >= stage && nowUtc <= dueAt && (lastReminderAtUtc is null || lastReminderAtUtc < stage))
                return new ReminderDecision(true, false);
        }

        // Vencida: el primero al pasar el vencimiento y luego uno cada OverdueEveryDays.
        if (nowUtc > dueAt)
        {
            var overdueDue =
                lastReminderAtUtc is null
                || lastReminderAtUtc < dueAt
                || nowUtc - lastReminderAtUtc.Value >= TimeSpan.FromDays(options.OverdueEveryDays);
            if (overdueDue)
                return new ReminderDecision(true, true);
        }

        return new ReminderDecision(false, isOverdue);
    }

    // Con fecha civil, la etapa es a las 9:00 de la oficina N días antes (como el aviso de tareas, P32);
    // restarle días al instante (fin del día) la dejaba cerca de la medianoche.
    private static DateTime StageAt(DateTime dueAtUtc, int days, DateOnly? dueOn, ResolvedTimeZone? officeZone) =>
        dueOn is { } civil && officeZone is not null
            ? WallTime
                .ToUtc(civil.AddDays(-days), new TimeOnly(9, 0), officeZone, NonexistentTimePolicy.ShiftForward)
                .Value
            : dueAtUtc.AddDays(-days);
}
