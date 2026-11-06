using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using Xunit;
using static TaxVision.Calendar.Tests.Application.ReminderTargetsTestData;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Al mover una cita, Reminder tiene que quedar con un aviso por ocurrencia vigente: se cierra el
/// objetivo viejo y se pide el nuevo con una clave de generación nueva. Se prueba por el handler.
/// </summary>
public sealed class RescheduleApplierTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();

    [Fact]
    public async Task OneOffRescheduled_ClosesOldTargetAndRequestsNew_NoMovedEvent()
    {
        var day = DateTime.UtcNow.Date.AddDays(3);
        var tenAm = DateTime.SpecifyKind(day.AddHours(14), DateTimeKind.Utc);
        var threePm = tenAm.AddHours(5);
        var appointment = PointInTime(_tenant, _organizer, tenAm, leadMinutes: 30);
        var bus = new FakeMessageBus();

        var result = await Reschedule(appointment, bus, OneOff(appointment, threePm));

        Assert.True(result.IsSuccess);
        var closed = Assert.Single(bus.Published.OfType<ReminderTargetClosedIntegrationEvent>());
        Assert.Equal(OccurrenceTargetId.For(appointment.Id, tenAm), closed.TargetId);
        var requested = Assert.Single(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Equal(OccurrenceTargetId.For(appointment.Id, threePm), requested.TargetId);
        Assert.EndsWith(":g1", requested.RequestKey);
        Assert.Equal(30, requested.LeadMinutes);
        Assert.Empty(bus.Published.OfType<ReminderTargetMovedIntegrationEvent>());
    }

    /// <summary>Mismo inicio, otro fin: el aviso vivo sigue valiendo y no se pide otro.</summary>
    [Fact]
    public async Task OneOffEndChangedOnly_LeavesTheLiveReminderAlone()
    {
        var start = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(3).AddHours(14), DateTimeKind.Utc);
        var appointment = PointInTime(_tenant, _organizer, start, leadMinutes: 30);
        var bus = new FakeMessageBus();
        var command = OneOff(appointment, start) with { NewEndUtc = start.AddHours(2) };

        var result = await Reschedule(appointment, bus, command);

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Published.OfType<ReminderTargetClosedIntegrationEvent>());
        Assert.Empty(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        Assert.Empty(bus.Published.OfType<ReminderTargetMovedIntegrationEvent>());
    }

    /// <summary>CAL-7: misma hora, otros minutos: se mueve el aviso vivo con los minutos nuevos.</summary>
    [Fact]
    public async Task OneOffLeadChangedOnly_PublishesTargetMovedWithLeadMinutes()
    {
        var start = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(3).AddHours(14), DateTimeKind.Utc);
        var appointment = PointInTime(_tenant, _organizer, start, leadMinutes: 30);
        var bus = new FakeMessageBus();
        var command = OneOff(appointment, start) with { ReminderLeadMinutes = 60 };

        var result = await Reschedule(appointment, bus, command);

        Assert.True(result.IsSuccess);
        var moved = Assert.Single(bus.Published.OfType<ReminderTargetMovedIntegrationEvent>());
        Assert.Equal(OccurrenceTargetId.For(appointment.Id, start), moved.TargetId);
        Assert.Equal(start, moved.NewAnchorAtUtc);
        Assert.Equal(60, moved.LeadMinutes);
        Assert.Empty(bus.Published.OfType<ReminderTargetClosedIntegrationEvent>());
        Assert.Empty(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task EditEntireSeries_ClosesChangedOccurrences_RequestsNewOnes()
    {
        var monday = Next(DayOfWeek.Monday);
        var tuesday = monday.AddDays(1);
        var series = Weekly(_tenant, _organizer, monday, leadMinutes: 30);
        var bus = new FakeMessageBus();
        var mondays = WeeklyTargets(series.Id, monday);
        var tuesdays = WeeklyTargets(series.Id, tuesday);

        var result = await Reschedule(series, bus, EntireSeries(series, tuesday));

        Assert.True(result.IsSuccess);
        Assert.InRange(mondays.Count, 8, 9);
        var closed = bus.Published.OfType<ReminderTargetClosedIntegrationEvent>().Select(c => c.TargetId).ToHashSet();
        Assert.Equal(mondays, closed);
        var requested = bus.Published.OfType<ReminderRequestedIntegrationEvent>().ToList();
        Assert.Equal(tuesdays, requested.Select(r => r.TargetId!.Value).ToHashSet());
        Assert.All(requested, r => Assert.EndsWith(":g1", r.RequestKey));
        Assert.Empty(bus.Published.OfType<ReminderTargetMovedIntegrationEvent>());
    }

    /// <summary>La serie nueva del corte es la misma reunión: hereda el aviso y lo pide en el acto.</summary>
    [Fact]
    public async Task SplitSeries_FollowerInheritsLeadAndRequestsItsReminders()
    {
        var monday = Next(DayOfWeek.Monday);
        var series = Weekly(_tenant, _organizer, monday, leadMinutes: 30);
        var appointments = new InMemoryAppointments(series);
        var bus = new FakeMessageBus();
        var cut = WeeklyTargetsStart(monday.AddDays(14));
        var command = EntireSeries(series, monday.AddDays(15)) with
        {
            Scope = EditScope.ThisAndFollowing,
            OriginalStartUtc = cut,
        };

        var result = await RescheduleAppointmentHandler.Handle(
            command,
            appointments,
            new CountingUnitOfWork(),
            new NullCalendarMetrics(),
            CancellationToken.None
        );
        await CalendarDomainEventReplay.DispatchIntoAsync(bus, appointments, appointments.All);

        Assert.True(result.IsSuccess);
        var follower = Assert.Single(appointments.All, a => a.Id != series.Id);
        Assert.Equal(30, follower.ReminderLeadMinutes);
        var requested = bus.Published.OfType<ReminderRequestedIntegrationEvent>().ToList();
        Assert.Equal(
            WeeklyTargets(follower.Id, monday.AddDays(15)),
            requested.Select(r => r.TargetId!.Value).ToHashSet()
        );
        Assert.Empty(bus.Published.OfType<ReminderTargetMovedIntegrationEvent>());
    }

    private static DateTime WeeklyTargetsStart(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TenAm), TimeZoneInfo.FindSystemTimeZoneById(NewYork));

    private async Task<Result<AppointmentResponse>> Reschedule(
        Appointment appointment,
        FakeMessageBus bus,
        RescheduleAppointmentCommand command
    )
    {
        var appointments = new InMemoryAppointments(appointment);
        var result = await RescheduleAppointmentHandler.Handle(
            command,
            appointments,
            new CountingUnitOfWork(),
            new NullCalendarMetrics(),
            CancellationToken.None
        );
        await CalendarDomainEventReplay.DispatchIntoAsync(bus, appointments, appointments.All);
        return result;
    }

    private RescheduleAppointmentCommand OneOff(Appointment appointment, DateTime newStartUtc) =>
        new(
            _tenant,
            appointment.Id,
            _organizer,
            null,
            null,
            newStartUtc,
            newStartUtc.AddHours(1),
            null,
            null,
            null,
            null,
            null,
            false
        );

    private RescheduleAppointmentCommand EntireSeries(Appointment series, DateOnly newFirstDate) =>
        new(
            _tenant,
            series.Id,
            _organizer,
            EditScope.EntireSeries,
            null,
            null,
            null,
            newFirstDate,
            TenAm,
            TimeSpan.FromHours(1),
            NewYork,
            RuleFor(newFirstDate).Value,
            false
        );
}
