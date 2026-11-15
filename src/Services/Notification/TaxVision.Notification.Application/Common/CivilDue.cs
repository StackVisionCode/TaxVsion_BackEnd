using BuildingBlocks.TimeZones.Formatting;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Fecha civil de un vencimiento para avisos y correos: con el corte de fin del día de la oficina, el prefijo
/// UTC del instante ya no es la fecha. Eventos de antes, sin fecha civil: medianoche UTC es esa fecha (P5).
/// </summary>
public static class CivilDue
{
    public static DateOnly? Of(DateOnly? dueOn, DateTime? dueAtUtc) =>
        dueOn ?? (dueAtUtc is { } instant ? DateOnly.FromDateTime(instant) : null);

    /// <summary>Fecha civil tipada para Scribe (<c>{"$date": "2026-11-15"}</c>); null sin vencimiento.</summary>
    public static Dictionary<string, object?>? Typed(DateOnly? dueOn, DateTime? dueAtUtc) =>
        ScribeDates.Date(Of(dueOn, dueAtUtc));

    /// <summary>"Nov 15, 2026"; null sin vencimiento.</summary>
    public static string? Text(DateOnly? dueOn, DateTime? dueAtUtc, string? language = null) =>
        Of(dueOn, dueAtUtc) is { } date ? ZonedTimeFormatter.CivilDate(date, language) : null;
}
