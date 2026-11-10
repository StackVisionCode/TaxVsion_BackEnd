using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Domain.Office;

/// <summary>Un día de atención de la oficina, en su zona.</summary>
public sealed record OfficeHours(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);

/// <summary>Un cierre de la oficina: de <see cref="Date"/> a <see cref="EndDate"/> (incluido), quizá cada año.</summary>
public sealed record OfficeClosureSpan(DateOnly Date, DateOnly? EndDate, string Label, bool RepeatsYearly);

/// <summary>Horario y cierres de la oficina en su zona.</summary>
public sealed record OfficeSchedule(
    string ZoneId,
    IReadOnlyList<OfficeHours> Hours,
    IReadOnlyList<OfficeClosureSpan> Closures
)
{
    public static OfficeSchedule Of(
        string zoneId,
        IReadOnlyList<OfficeHours> hours,
        IReadOnlyList<OfficeClosureSpan> closures
    ) => new(zoneId, hours, closures);

    public bool HasHours => Hours.Count > 0;
}

/// <summary>
/// La agenda posible de un preparador dentro de la oficina (D-CAL-D): sus franjas ∩ el horario de la
/// oficina, sin los días de cierre. Sin horario de la oficina solo se quitan los cierres; sin datos de
/// la oficina, las franjas quedan como están. Se recorre en la zona de la oficina por el cambio de hora.
/// </summary>
public static class OfficeCalendar
{
    public static IReadOnlyList<TimeSlot> Restrict(
        IReadOnlyList<TimeSlot> windows,
        DateTime fromUtc,
        DateTime toUtc,
        OfficeSchedule? office
    )
    {
        if (office is null || (!office.HasHours && office.Closures.Count == 0))
            return windows;

        var zone = CalendarTimeZone.Create(office.ZoneId);
        if (zone.IsFailure)
            return windows;

        var firstDay = LocalDay(fromUtc, zone.Value).AddDays(-1);
        var lastDay = LocalDay(toUtc, zone.Value).AddDays(1);

        return office.HasHours
            ? Intersect(windows, OpenWindows(office, zone.Value, firstDay, lastDay))
            : Subtract(windows, ClosedWindows(office, zone.Value, firstDay, lastDay));
    }

    /// <summary>Si ese día local cae en un cierre; devuelve su etiqueta.</summary>
    public static bool IsClosed(DateOnly day, IReadOnlyList<OfficeClosureSpan> closures, out string? label)
    {
        foreach (var closure in closures)
        {
            if (Covers(closure, day))
            {
                label = closure.Label;
                return true;
            }
        }

        label = null;
        return false;
    }

    private static bool Covers(OfficeClosureSpan closure, DateOnly day)
    {
        var end = closure.EndDate ?? closure.Date;
        if (!closure.RepeatsYearly)
            return day >= closure.Date && day <= end;

        // Cada año desde el primero; un tramo que cruza el año nuevo se prueba también desde el anterior.
        foreach (var year in new[] { day.Year, day.Year - 1 })
        {
            var shift = year - closure.Date.Year;
            if (shift < 0)
                continue;
            var start = closure.Date.AddYears(shift);
            if (day >= start && day <= end.AddYears(shift))
                return true;
        }

        return false;
    }

    private static List<TimeSlot> OpenWindows(
        OfficeSchedule office,
        CalendarTimeZone zone,
        DateOnly first,
        DateOnly last
    )
    {
        var open = new List<TimeSlot>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (IsClosed(day, office.Closures, out _))
                continue;
            foreach (var hours in office.Hours)
            {
                if (hours.Day != day.DayOfWeek)
                    continue;
                var start = WallClock.ToUtcShiftingOverGaps(day, hours.Opens, zone);
                var end = WallClock.ToUtcShiftingOverGaps(day, hours.Closes, zone);
                if (start.IsSuccess && end.IsSuccess && end.Value > start.Value)
                    open.Add(new TimeSlot(start.Value, end.Value));
            }
        }

        return open;
    }

    private static List<TimeSlot> ClosedWindows(
        OfficeSchedule office,
        CalendarTimeZone zone,
        DateOnly first,
        DateOnly last
    )
    {
        var closed = new List<TimeSlot>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (!IsClosed(day, office.Closures, out _))
                continue;
            var start = WallClock.ToUtcShiftingOverGaps(day, TimeOnly.MinValue, zone);
            var end = WallClock.ToUtcShiftingOverGaps(day.AddDays(1), TimeOnly.MinValue, zone);
            if (start.IsSuccess && end.IsSuccess)
                closed.Add(new TimeSlot(start.Value, end.Value));
        }

        return closed;
    }

    private static List<TimeSlot> Intersect(IReadOnlyList<TimeSlot> windows, List<TimeSlot> allowed)
    {
        var result = new List<TimeSlot>();
        foreach (var window in windows)
        {
            foreach (var open in allowed)
            {
                var start = window.StartUtc > open.StartUtc ? window.StartUtc : open.StartUtc;
                var end = window.EndUtc < open.EndUtc ? window.EndUtc : open.EndUtc;
                if (end > start)
                    result.Add(new TimeSlot(start, end));
            }
        }

        result.Sort((left, right) => left.StartUtc.CompareTo(right.StartUtc));
        return result;
    }

    private static List<TimeSlot> Subtract(IReadOnlyList<TimeSlot> windows, List<TimeSlot> removed)
    {
        var result = new List<TimeSlot>(windows);
        foreach (var cut in removed)
        {
            var next = new List<TimeSlot>();
            foreach (var window in result)
            {
                if (cut.EndUtc <= window.StartUtc || cut.StartUtc >= window.EndUtc)
                {
                    next.Add(window);
                    continue;
                }
                if (cut.StartUtc > window.StartUtc)
                    next.Add(new TimeSlot(window.StartUtc, cut.StartUtc));
                if (cut.EndUtc < window.EndUtc)
                    next.Add(new TimeSlot(cut.EndUtc, window.EndUtc));
            }
            result = next;
        }

        return result;
    }

    private static DateOnly LocalDay(DateTime utc, CalendarTimeZone zone)
    {
        var local = WallClock.ToWallClock(utc, zone);
        return DateOnly.FromDateTime(local.IsSuccess ? local.Value : utc);
    }
}
