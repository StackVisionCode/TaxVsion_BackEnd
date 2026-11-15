using TaxVision.Calendar.Application.Appointments.DomainEvents;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using TaxVision.Calendar.Domain.ValueObjects;
using Wolverine;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Qué hora anuncian los correos de una serie: la próxima ocurrencia, no la primera de la regla (que en noviembre
/// dice "5 de enero"). La cancelación dice qué se canceló y en qué zona.
/// </summary>
public sealed class AppointmentEventsTests
{
    private const string NewYork = "America/New_York";

    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly DateTime January = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Martes 10 de noviembre de 2026: el próximo lunes es el 16, 9:00 en Nueva York (14:00Z, EST).
    private static readonly DateTime November = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NextMonday = new(2026, 11, 16, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Inviting_to_a_series_announces_its_next_occurrence_not_the_first()
    {
        var series = MondaysAtNine();
        series.AddAttendee(
            AttendeeKind.External,
            null,
            null,
            AttendeeSnapshot.Create("Ana Cliente", "ana@client.test").Value,
            isRequired: true,
            Organizer,
            January,
            canManageAll: false
        );
        var invited = series.DomainEvents.OfType<AppointmentAttendeeInvitedDomainEvent>().Last();

        var added = await AppointmentAttendeeInvitedDomainEventHandler.Handle(
            invited,
            new InMemoryAppointments(series),
            new Envelope(),
            default
        );

        // El handler usa el reloj real: la hora anunciada es un lunes a las 9:00 en Nueva York que todavía no pasó.
        var start = Assert.IsType<DateTime>(added!.StartUtc);
        Assert.True(start >= DateTime.UtcNow.AddMinutes(-1), $"announced {start:O}, in the past");
        var local = TimeZoneInfo.ConvertTimeFromUtc(start, TimeZoneInfo.FindSystemTimeZoneById(NewYork));
        Assert.Equal(DayOfWeek.Monday, local.DayOfWeek);
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(local));
    }

    [Fact]
    public void Moving_the_entire_series_announces_the_next_occurrence_before_and_after()
    {
        var series = MondaysAtNine();

        Assert.True(
            series
                .EditEntireSeries(
                    EventTiming
                        .RecurringOf(new DateOnly(2026, 1, 5), new TimeOnly(10, 0), TimeSpan.FromHours(1), NewYork)
                        .Value,
                    RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
                    Organizer,
                    canManageAll: false,
                    November
                )
                .IsSuccess
        );

        var moved = series.DomainEvents.OfType<AppointmentRescheduledDomainEvent>().Last();
        Assert.Equal(NextMonday, moved.PreviousStartUtc);
        Assert.Equal(NextMonday.AddHours(1), moved.NewStartUtc);
    }

    [Fact]
    public void Moving_a_series_that_has_not_started_announces_its_first_occurrence()
    {
        var series = MondaysAtNine();

        series.EditEntireSeries(
            EventTiming
                .RecurringOf(new DateOnly(2026, 1, 5), new TimeOnly(10, 0), TimeSpan.FromHours(1), NewYork)
                .Value,
            RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
            Organizer,
            canManageAll: false,
            January
        );

        var moved = series.DomainEvents.OfType<AppointmentRescheduledDomainEvent>().Last();
        Assert.Equal(new DateTime(2026, 1, 5, 14, 0, 0, DateTimeKind.Utc), moved.PreviousStartUtc);
        Assert.Equal(new DateTime(2026, 1, 5, 15, 0, 0, DateTimeKind.Utc), moved.NewStartUtc);
    }

    [Fact]
    public async Task Cancelling_an_occurrence_says_which_one_and_in_which_zone()
    {
        var series = MondaysAtNine();
        Assert.True(series.CancelOccurrence(NextMonday, Organizer, canManageAll: false, November).IsSuccess);
        var domainEvent = series.DomainEvents.OfType<AppointmentOccurrenceCancelledDomainEvent>().Last();

        var messages = await AppointmentOccurrenceCancelledDomainEventHandler.Handle(
            domainEvent,
            new InMemoryAppointments(series),
            new Envelope(),
            default
        );

        var cancelled = Assert.Single(
            messages.OfType<BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentCancelledIntegrationEvent>()
        );

        Assert.Equal(NextMonday, cancelled.StartUtc);
        Assert.Equal(NewYork, cancelled.TimeZoneId);
    }

    [Fact]
    public async Task Cancelling_a_single_appointment_says_when_it_was()
    {
        var start = new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc);
        var single = Appointment
            .Schedule(
                Guid.NewGuid(),
                AppointmentTitle.Create("Consulta").Value,
                EventTiming.PointInTimeOf(start, start.AddHours(1), NewYork).Value,
                Guid.NewGuid(),
                Organizer,
                January
            )
            .Value;
        Assert.True(single.Cancel(Organizer, canManageAll: false, null, November).IsSuccess);

        var cancelled = await Publish(single);

        Assert.Equal(start, cancelled.StartUtc);
        Assert.Equal(NewYork, cancelled.TimeZoneId);
    }

    private static async Task<BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentCancelledIntegrationEvent> Publish(
        Appointment appointment
    )
    {
        var domainEvent = appointment.DomainEvents.OfType<AppointmentCancelledDomainEvent>().Last();
        var published = await AppointmentCancelledDomainEventHandler.Handle(
            domainEvent,
            new InMemoryAppointments(appointment),
            new Envelope(),
            default
        );
        return Assert.IsType<BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentCancelledIntegrationEvent>(
            published
        );
    }

    private static Appointment MondaysAtNine()
    {
        var timing = EventTiming
            .RecurringOf(new DateOnly(2026, 1, 5), new TimeOnly(9, 0), TimeSpan.FromHours(1), NewYork)
            .Value;
        var series = Appointment
            .Schedule(
                Guid.NewGuid(),
                AppointmentTitle.Create("Revision semanal").Value,
                timing,
                Guid.NewGuid(),
                Organizer,
                January
            )
            .Value;
        series.MakeRecurring(
            RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
            timing,
            Organizer,
            canManageAll: false
        );
        return series;
    }
}
