using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Types;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

public sealed class ScheduleAppointmentRulesTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private const string NewYork = "America/New_York";

    [Fact]
    public async Task Without_an_end_the_type_duration_sets_it()
    {
        var type = Type(TimeSpan.FromMinutes(45), dailyCap: null);
        var start = Future(10);

        var result = await Schedule(type, new InMemoryAppointments(), start, end: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(start.AddMinutes(45), result.Value.Appointment.EndUtc);
    }

    [Fact]
    public async Task The_daily_cap_counts_the_organizers_local_day_for_that_type()
    {
        var type = Type(TimeSpan.FromMinutes(15), dailyCap: 1);
        var appointments = new InMemoryAppointments();

        var first = await Schedule(type, appointments, Future(10), end: null);
        Assert.True(first.IsSuccess);

        // Las 22:00 de Nueva York ya son el día siguiente en UTC, pero es el mismo día del organizador.
        var sameDay = await Schedule(type, appointments, Future(22), end: null);
        Assert.Equal(AppointmentErrors.DailyCapReached, sameDay.Error);

        var nextDay = await Schedule(type, appointments, Future(10).AddDays(1), end: null);
        Assert.True(nextDay.IsSuccess);
    }

    [Fact]
    public async Task A_cancelled_appointment_frees_its_place_in_the_cap()
    {
        var type = Type(TimeSpan.FromMinutes(15), dailyCap: 1);
        var appointments = new InMemoryAppointments();
        var first = await Schedule(type, appointments, Future(10), end: null);
        appointments
            .All.Single(a => a.Id == first.Value.Appointment.Id)
            .Cancel(_organizer, false, null, DateTime.UtcNow);

        var again = await Schedule(type, appointments, Future(11), end: null);

        Assert.True(again.IsSuccess);
    }

    /// <summary>
    /// Una cita no se agenda en el pasado (ni esta mañana). Cinco minutos de margen por el reloj y el
    /// tiempo de llenar el formulario.
    /// </summary>
    [Fact]
    public async Task An_appointment_cannot_start_in_the_past_but_a_few_minutes_of_margin_are_fine()
    {
        var type = Type(TimeSpan.FromMinutes(30), dailyCap: null);

        var thisMorning = await Schedule(type, new InMemoryAppointments(), DateTime.UtcNow.AddHours(-3), end: null);
        var yesterday = await Schedule(type, new InMemoryAppointments(), DateTime.UtcNow.AddDays(-1), end: null);
        var justNow = await Schedule(type, new InMemoryAppointments(), DateTime.UtcNow.AddMinutes(-2), end: null);

        Assert.Equal(TimingErrors.StartsInThePast, thisMorning.Error);
        Assert.Equal(TimingErrors.StartsInThePast, yesterday.Error);
        Assert.True(justNow.IsSuccess);
    }

    /// <summary>Hora de pared de Nueva York, dentro de tres días.</summary>
    private static DateTime Future(int localHour)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(NewYork);
        var day = DateTime.UtcNow.Date.AddDays(3);
        var local = new DateTime(day.Year, day.Month, day.Day, localHour, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private AppointmentType Type(TimeSpan duration, int? dailyCap) =>
        AppointmentType.Create(_tenant, "Drop-off", duration, "#336699", DateTime.UtcNow, dailyCap: dailyCap).Value;

    private Task<Result<AppointmentWithWarnings>> Schedule(
        AppointmentType type,
        InMemoryAppointments appointments,
        DateTime start,
        DateTime? end
    ) =>
        ScheduleAppointmentHandler.Handle(
            new ScheduleAppointmentCommand(
                _tenant,
                _organizer,
                "Drop-off",
                null,
                null,
                type.Id,
                NewYork,
                start,
                end,
                null,
                null,
                null,
                null,
                null,
                null,
                false
            ),
            appointments,
            new SingleTypeRepository(type),
            new EmptyAvailability(),
            new NoScheduleLock(),
            new CountingUnitOfWork(),
            new NullCalendarMetrics(),
            CancellationToken.None
        );
}
