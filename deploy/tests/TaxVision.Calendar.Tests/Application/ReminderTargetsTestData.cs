using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>Citas y objetivos esperados para las pruebas de avisos, relativos al reloj real.</summary>
internal static class ReminderTargetsTestData
{
    public const string NewYork = "America/New_York";
    public const int HorizonDays = 60;

    public static readonly TimeOnly TenAm = new(10, 0);

    /// <summary>El primer día de la semana pedido, con al menos dos días de margen.</summary>
    public static DateOnly Next(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        while (date.DayOfWeek != day)
            date = date.AddDays(1);
        return date;
    }

    public static Appointment PointInTime(Guid tenant, Guid organizer, DateTime startUtc, int? leadMinutes)
    {
        var timing = EventTiming.PointInTimeOf(startUtc, startUtc.AddHours(1), NewYork).Value;
        var appointment = Appointment
            .Schedule(
                tenant,
                AppointmentTitle.Create("Client review").Value,
                timing,
                Guid.NewGuid(),
                organizer,
                DateTime.UtcNow
            )
            .Value;
        appointment.RequestReminder(leadMinutes);
        return appointment;
    }

    public static Appointment Weekly(Guid tenant, Guid organizer, DateOnly firstDate, int? leadMinutes)
    {
        var timing = EventTiming.RecurringOf(firstDate, TenAm, TimeSpan.FromHours(1), NewYork).Value;
        var series = Appointment
            .Schedule(
                tenant,
                AppointmentTitle.Create("Weekly review").Value,
                timing,
                Guid.NewGuid(),
                organizer,
                DateTime.UtcNow
            )
            .Value;
        series.MakeRecurring(RuleFor(firstDate), timing, organizer, canManageAll: false);
        series.RequestReminder(leadMinutes);
        return series;
    }

    public static RecurrenceRule RuleFor(DateOnly firstDate) =>
        RecurrenceRule.Create($"FREQ=WEEKLY;BYDAY={firstDate.DayOfWeek.ToString()[..2].ToUpperInvariant()}").Value;

    /// <summary>Los ids de cada ocurrencia semanal a las 10:00 de Nueva York dentro del horizonte.</summary>
    public static HashSet<Guid> WeeklyTargets(Guid appointmentId, DateOnly firstDate)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(NewYork);
        var now = DateTime.UtcNow;
        var targets = new HashSet<Guid>();
        for (var date = firstDate; ; date = date.AddDays(7))
        {
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TenAm), zone);
            if (startUtc >= now.AddDays(HorizonDays))
                break;
            if (startUtc.AddHours(1) > now)
                targets.Add(OccurrenceTargetId.For(appointmentId, DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)));
        }

        return targets;
    }
}
