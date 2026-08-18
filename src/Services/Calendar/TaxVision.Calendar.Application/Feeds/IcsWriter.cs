using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using IcalCalendar = Ical.Net.Calendar;

namespace TaxVision.Calendar.Application.Feeds;

/// <summary>
/// Escribe el `.ics` del feed.
///
/// <para>
/// Serializa con Ical.Net y no arma el texto a mano: una `EXDATE` mal escrita no rompe el import, lo
/// deja a medias — el cliente se traga el archivo y muestra la ocurrencia que se canceló.
/// </para>
///
/// <para>
/// Las series salen como <b>una</b> VEVENT con su RRULE, no como una VEVENT por ocurrencia. Expandirlas
/// acá dejaría al cliente sin saber que son una serie, y una serie sin fin produciría un archivo sin
/// fin. Las excepciones viajan como EXDATE (cancelada) y como VEVENT con RECURRENCE-ID (movida).
/// </para>
/// </summary>
public static class IcsWriter
{
    public static string Write(IReadOnlyList<Appointment> appointments)
    {
        var calendar = new IcalCalendar();
        var zones = new HashSet<string>(StringComparer.Ordinal);

        foreach (var appointment in appointments)
        {
            if (appointment.Status == AppointmentStatus.Cancelled)
                continue;

            var zone = appointment.Timing.TimeZone;
            if (zones.Add(zone.Id))
                calendar.AddTimeZone(zone.Id);

            if (appointment.Recurrence is null)
            {
                if (Single(appointment) is { } single)
                    calendar.Events.Add(single);

                continue;
            }

            calendar.Events.Add(Series(appointment));

            foreach (var exception in appointment.Exceptions)
            {
                if (exception.Kind == ExceptionKind.Overridden && Moved(appointment, exception) is { } moved)
                    calendar.Events.Add(moved);
            }
        }

        return new CalendarSerializer().SerializeToString(calendar);
    }

    private static CalendarEvent? Single(Appointment appointment)
    {
        var timing = appointment.Timing;

        if (timing.Kind == TimingKind.AllDay)
        {
            if (timing.StartDate is not { } startDate || timing.EndDate is not { } endDate)
                return null;

            var allDay = Base(appointment);
            allDay.Start = new CalDateTime(startDate);

            // DTEND de un all-day es exclusiva; EndDate es inclusiva de cara al usuario.
            allDay.End = new CalDateTime(endDate.AddDays(1));
            return allDay;
        }

        if (timing.StartUtc is not { } startUtc || timing.EndUtc is not { } endUtc)
            return null;

        var pointInTime = Base(appointment);
        pointInTime.Start = InZone(startUtc, timing.TimeZone);
        pointInTime.End = InZone(endUtc, timing.TimeZone);
        return pointInTime;
    }

    private static CalendarEvent Series(Appointment appointment)
    {
        var timing = appointment.Timing;
        var seriesStart = timing.SeriesStartDate!.Value.ToDateTime(
            timing.LocalStartTime!.Value,
            DateTimeKind.Unspecified
        );
        var duration = timing.Duration ?? TimeSpan.FromHours(1);

        var series = Base(appointment);
        series.Start = new CalDateTime(seriesStart, timing.TimeZone.Id);
        series.End = new CalDateTime(seriesStart.Add(duration), timing.TimeZone.Id);
        series.RecurrenceRules = [new RecurrencePattern(appointment.Recurrence!.Value)];

        foreach (var exception in appointment.Exceptions)
        {
            if (exception.Kind == ExceptionKind.Cancelled)
                series.ExceptionDates.Add(InZone(exception.OriginalStartUtc, timing.TimeZone));
        }

        return series;
    }

    /// <summary>La ocurrencia movida: mismo UID, y el RECURRENCE-ID apunta a dónde estaba.</summary>
    private static CalendarEvent? Moved(Appointment appointment, AppointmentException exception)
    {
        if (exception.NewStartUtc is not { } newStart)
            return null;

        var zone = appointment.Timing.TimeZone;
        var duration = appointment.Timing.Duration ?? TimeSpan.FromHours(1);

        var moved = Base(appointment);
        moved.RecurrenceId = InZone(exception.OriginalStartUtc, zone);
        moved.Start = InZone(newStart, zone);
        moved.End = InZone(exception.NewEndUtc ?? newStart.Add(duration), zone);

        if (exception.NewTitle is { Length: > 0 } title)
            moved.Summary = title;

        if (exception.NewLocation is { Length: > 0 } location)
            moved.Location = location;

        return moved;
    }

    private static CalendarEvent Base(Appointment appointment) =>
        new()
        {
            Uid = appointment.Id.ToString(),
            Summary = appointment.Title.Value,
            Description = appointment.Description,
            Location = appointment.Location?.Value,
        };

    private static CalDateTime InZone(DateTime utc, CalendarTimeZone zone)
    {
        var wall = WallClock.ToWallClock(utc, zone);
        return wall.IsSuccess ? new CalDateTime(wall.Value, zone.Id) : new CalDateTime(utc, "UTC");
    }
}
