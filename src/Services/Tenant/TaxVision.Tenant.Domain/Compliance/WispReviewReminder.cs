using BuildingBlocks.TimeZones;

namespace TaxVision.Tenant.Domain.Compliance;

/// <summary>Etapa del recordatorio anual del WISP.</summary>
public enum WispReviewReminderStage
{
    /// <summary>Faltan <see cref="WispReviewSchedule.DueSoonDays"/> días o menos.</summary>
    DueSoon,

    /// <summary>Ya venció; se repite cada <see cref="WispReviewSchedule.OverdueRepeatDays"/> días.</summary>
    Overdue,
}

/// <summary>Aviso que toca mandar: a qué vencimiento corresponde y en qué etapa.</summary>
public sealed record WispReviewReminder(DateOnly DueOn, WispReviewReminderStage Stage);

/// <summary>Reglas fijas del recordatorio (decisión del dueño, contrato F4).</summary>
public static class WispReviewSchedule
{
    public const int ReviewIntervalMonths = 12;
    public const int DueSoonDays = 30;
    public const int OverdueRepeatDays = 30;

    /// <summary>"Hoy" en el calendario de la oficina. Zona inválida o vacía → UTC.</summary>
    public static DateOnly OfficeToday(DateTime nowUtc, string? officeTimeZoneId)
    {
        var utc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var zone = IanaTimeZone.TryFindTimeZone(officeTimeZoneId, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    }
}
