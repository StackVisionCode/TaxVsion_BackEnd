using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Appointments.Consumers;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;
using static TaxVision.Calendar.Tests.Application.ReminderTargetsTestData;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Un aviso por acción: suma lo publicado a mano y lo devuelto por los handlers de eventos de dominio. Si
/// quedan los dos caminos, el aviso sale dos veces. Ningún evento de dominio sale por el bus: su camino es la
/// cola domain-events, no Rabbit.
/// </summary>
public sealed class AppointmentCommandEventCountTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly Guid Guest = Guid.NewGuid();
    private static readonly DateTime StartUtc = TruncatedToSeconds(DateTime.UtcNow.AddDays(3));

    [Fact]
    public async Task Schedule_EmitsExactlyOneScheduledAndItsReminder()
    {
        var fixture = new Fixture();

        await fixture.RunAsync(typeof(ScheduleAppointmentHandler), ScheduleCommand(leadMinutes: 30));

        var emitted = await fixture.EmittedAsync();
        var scheduled = Assert.Single(emitted.OfType<AppointmentScheduledIntegrationEvent>());
        Assert.Equal(StartUtc, scheduled.StartUtc);
        Assert.Equal(Organizer, scheduled.OrganizerUserId);
        Assert.Equal(StartUtc, Assert.Single(emitted.OfType<ReminderRequestedIntegrationEvent>()).AnchorAtUtc);
    }

    [Fact]
    public async Task Reschedule_EmitsExactlyOneRescheduledWithBothInstants()
    {
        var appointment = OneOff(leadMinutes: 30);
        var fixture = new Fixture(appointment);
        var newStart = StartUtc.AddHours(5);

        await fixture.RunAsync(typeof(RescheduleAppointmentHandler), MoveOneOff(appointment, newStart));

        var emitted = await fixture.EmittedAsync();
        var moved = Assert.Single(emitted.OfType<AppointmentRescheduledIntegrationEvent>());
        Assert.Equal(StartUtc, moved.PreviousStartUtc);
        Assert.Equal(newStart, moved.NewStartUtc);
        Assert.Equal(newStart.AddHours(1), moved.NewEndUtc);
        Assert.Equal(nameof(EditScope.EntireSeries), moved.Scope);
        Assert.Contains(moved.Recipients, r => r.UserId == Guest);
        Assert.Equal(
            OccurrenceTargetId.For(appointment.Id, StartUtc),
            Assert.Single(emitted.OfType<ReminderTargetClosedIntegrationEvent>()).TargetId
        );
        Assert.Equal(
            OccurrenceTargetId.For(appointment.Id, newStart),
            Assert.Single(emitted.OfType<ReminderRequestedIntegrationEvent>()).TargetId
        );
    }

    [Fact]
    public async Task RescheduleEntireSeries_AnnouncesFirstOccurrences()
    {
        var monday = Next(DayOfWeek.Monday);
        var series = Weekly(Tenant, Organizer, monday, leadMinutes: 30);
        var fixture = new Fixture(series);

        await fixture.RunAsync(typeof(RescheduleAppointmentHandler), MoveSeries(series, monday.AddDays(1)));

        var emitted = await fixture.EmittedAsync();
        var moved = Assert.Single(emitted.OfType<AppointmentRescheduledIntegrationEvent>());
        Assert.Equal(UtcOf(monday), moved.PreviousStartUtc);
        Assert.Equal(UtcOf(monday.AddDays(1)), moved.NewStartUtc);
        Assert.Equal(UtcOf(monday.AddDays(1)).AddHours(1), moved.NewEndUtc);
        Assert.Equal(nameof(EditScope.EntireSeries), moved.Scope);
        Assert.Equal(
            WeeklyTargets(series.Id, monday),
            emitted.OfType<ReminderTargetClosedIntegrationEvent>().Select(c => c.TargetId).ToHashSet()
        );
        Assert.Equal(
            WeeklyTargets(series.Id, monday.AddDays(1)),
            emitted.OfType<ReminderRequestedIntegrationEvent>().Select(r => r.TargetId!.Value).ToHashSet()
        );
    }

    [Fact]
    public async Task RescheduleOccurrence_EmitsOneRescheduledAndOneMoved()
    {
        var monday = Next(DayOfWeek.Monday);
        var series = Weekly(Tenant, Organizer, monday, leadMinutes: 30);
        var fixture = new Fixture(series);
        var original = UtcOf(monday.AddDays(7));
        var newStart = original.AddHours(2);
        var command = MoveOneOff(series, newStart) with
        {
            Scope = EditScope.ThisOccurrence,
            OriginalStartUtc = original,
            NewEndUtc = null,
        };

        await fixture.RunAsync(typeof(RescheduleAppointmentHandler), command);

        var emitted = await fixture.EmittedAsync();
        var moved = Assert.Single(emitted.OfType<AppointmentRescheduledIntegrationEvent>());
        Assert.Equal(nameof(EditScope.ThisOccurrence), moved.Scope);
        Assert.Equal(original, moved.OriginalStartUtc);
        Assert.Equal(newStart.AddHours(1), moved.NewEndUtc);
        var target = Assert.Single(emitted.OfType<ReminderTargetMovedIntegrationEvent>());
        Assert.Equal(OccurrenceTargetId.For(series.Id, original), target.TargetId);
        Assert.Equal(newStart, target.NewAnchorAtUtc);
        Assert.Empty(emitted.OfType<ReminderRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task RescheduleFollowing_EmitsOneSplitAndOneRescheduledForTheNewSeries()
    {
        var monday = Next(DayOfWeek.Monday);
        var series = Weekly(Tenant, Organizer, monday, leadMinutes: 30);
        var fixture = new Fixture(series);
        var cut = UtcOf(monday.AddDays(14));
        var command = MoveSeries(series, monday.AddDays(15)) with
        {
            Scope = EditScope.ThisAndFollowing,
            OriginalStartUtc = cut,
        };

        await fixture.RunAsync(typeof(RescheduleAppointmentHandler), command);

        var emitted = await fixture.EmittedAsync();
        var follower = Assert.Single(fixture.Appointments.All, a => a.Id != series.Id);
        var split = Assert.Single(emitted.OfType<CalendarSeriesSplitIntegrationEvent>());
        Assert.Equal(series.Id, split.OriginalSeriesId);
        Assert.Equal(follower.Id, split.NewSeriesId);
        var moved = Assert.Single(emitted.OfType<AppointmentRescheduledIntegrationEvent>());
        Assert.Equal(follower.Id, moved.AppointmentId);
        Assert.Equal(nameof(EditScope.ThisAndFollowing), moved.Scope);
        Assert.Equal(cut, moved.PreviousStartUtc);
        Assert.Equal(UtcOf(monday.AddDays(15)), moved.NewStartUtc);
        Assert.Equal(
            WeeklyTargets(follower.Id, monday.AddDays(15)),
            emitted.OfType<ReminderRequestedIntegrationEvent>().Select(r => r.TargetId!.Value).ToHashSet()
        );
        var closed = emitted.OfType<ReminderTargetClosedIntegrationEvent>().ToList();
        Assert.NotEmpty(closed);
        Assert.Equal(closed.Count, closed.Select(c => c.TargetId).Distinct().Count());
        Assert.All(closed, c => Assert.Equal("series split", c.Reason));
    }

    [Fact]
    public async Task CancelEntire_EmitsOneCancelledAndClosesItsTarget()
    {
        var appointment = OneOff(leadMinutes: 30);
        var fixture = new Fixture(appointment);

        await fixture.RunAsync(
            typeof(CancelAppointmentHandler),
            new CancelAppointmentCommand(Tenant, appointment.Id, Organizer, null, null, "no-show", false)
        );

        var emitted = await fixture.EmittedAsync();
        var cancelled = Assert.Single(emitted.OfType<AppointmentCancelledIntegrationEvent>());
        Assert.Equal(nameof(EditScope.EntireSeries), cancelled.Scope);
        Assert.Equal("no-show", cancelled.Reason);
        Assert.Contains(cancelled.Recipients, r => r.UserId == Guest);
        Assert.Empty(emitted.OfType<OccurrenceCancelledIntegrationEvent>());
        var closed = Assert.Single(emitted.OfType<ReminderTargetClosedIntegrationEvent>());
        Assert.Equal(OccurrenceTargetId.For(appointment.Id, StartUtc), closed.TargetId);
        Assert.Equal("no-show", closed.Reason);
    }

    /// <summary>El alcance del pedido viaja tal cual, aunque la cita entera sea lo que se cancela.</summary>
    [Fact]
    public async Task CancelSeriesWithFollowingScope_KeepsTheScopeAndOriginal()
    {
        var monday = Next(DayOfWeek.Monday);
        var series = Weekly(Tenant, Organizer, monday, leadMinutes: null);
        var fixture = new Fixture(series);
        var original = UtcOf(monday.AddDays(7));

        await fixture.RunAsync(
            typeof(CancelAppointmentHandler),
            new CancelAppointmentCommand(
                Tenant,
                series.Id,
                Organizer,
                EditScope.ThisAndFollowing,
                original,
                null,
                false
            )
        );

        var cancelled = Assert.Single((await fixture.EmittedAsync()).OfType<AppointmentCancelledIntegrationEvent>());
        Assert.Equal(nameof(EditScope.ThisAndFollowing), cancelled.Scope);
        Assert.Equal(original, cancelled.OriginalStartUtc);
    }

    [Fact]
    public async Task CancelOccurrence_EmitsOneOfEachAndClosesOnlyThatTarget()
    {
        var monday = Next(DayOfWeek.Monday);
        var series = Weekly(Tenant, Organizer, monday, leadMinutes: 30);
        var fixture = new Fixture(series);
        var original = UtcOf(monday.AddDays(7));

        await fixture.RunAsync(
            typeof(CancelAppointmentHandler),
            new CancelAppointmentCommand(
                Tenant,
                series.Id,
                Organizer,
                EditScope.ThisOccurrence,
                original,
                "holiday",
                false
            )
        );

        var emitted = await fixture.EmittedAsync();
        var cancelled = Assert.Single(emitted.OfType<AppointmentCancelledIntegrationEvent>());
        Assert.Equal(nameof(EditScope.ThisOccurrence), cancelled.Scope);
        Assert.Equal(original, cancelled.OriginalStartUtc);
        Assert.Equal("holiday", cancelled.Reason);
        Assert.Equal(original, Assert.Single(emitted.OfType<OccurrenceCancelledIntegrationEvent>()).OriginalStartUtc);
        Assert.Equal(
            OccurrenceTargetId.For(series.Id, original),
            Assert.Single(emitted.OfType<ReminderTargetClosedIntegrationEvent>()).TargetId
        );
        Assert.Empty(emitted.OfType<ReminderRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task AddAttendee_EmitsExactlyOneAdded()
    {
        var appointment = OneOff(leadMinutes: null);
        var fixture = new Fixture(appointment);
        var client = Guid.NewGuid();

        await fixture.RunAsync(
            typeof(AddAttendeeHandler),
            new AddAttendeeCommand(
                Tenant,
                appointment.Id,
                Organizer,
                AttendeeKind.Customer,
                null,
                client,
                "Client",
                "client@example.com",
                true,
                false
            )
        );

        var added = Assert.Single((await fixture.EmittedAsync()).OfType<AppointmentAttendeeAddedIntegrationEvent>());
        Assert.Equal(nameof(AttendeeKind.Customer), added.AttendeeKind);
        Assert.Equal(client, added.CustomerId);
        Assert.Null(added.UserId);
        Assert.Equal("client@example.com", added.Email);
        Assert.Equal("Client review", added.Title);
        Assert.Equal(StartUtc, added.StartUtc);
        Assert.Equal(NewYork, added.TimeZoneId);
    }

    [Fact]
    public async Task Respond_EmitsExactlyOneResponseToTheOrganizer()
    {
        var appointment = OneOff(leadMinutes: null);
        var fixture = new Fixture(appointment);

        await fixture.RunAsync(
            typeof(RespondToAppointmentHandler),
            new RespondToAppointmentCommand(Tenant, appointment.Id, Guest, AttendeeResponse.Accepted)
        );

        var responded = Assert.Single(
            (await fixture.EmittedAsync()).OfType<AppointmentAttendeeRespondedIntegrationEvent>()
        );
        Assert.Equal(Organizer, responded.OrganizerUserId);
        Assert.Equal(Guest, responded.UserId);
        Assert.Equal(nameof(AttendeeResponse.Accepted), responded.Response);
    }

    [Fact]
    public async Task OffboardWithoutSuccessor_EmitsOneCancelledAndClosesTheReminder()
    {
        var appointment = OneOff(leadMinutes: 30);
        var fixture = new Fixture(appointment);

        await fixture.RunAsync(typeof(CalendarUserOffboardedConsumer), Offboarded(successor: null));

        var emitted = await fixture.EmittedAsync();
        var cancelled = Assert.Single(emitted.OfType<AppointmentCancelledIntegrationEvent>());
        Assert.Equal(nameof(EditScope.EntireSeries), cancelled.Scope);
        Assert.Null(cancelled.OriginalStartUtc);
        Assert.Equal(
            OccurrenceTargetId.For(appointment.Id, StartUtc),
            Assert.Single(emitted.OfType<ReminderTargetClosedIntegrationEvent>()).TargetId
        );
        Assert.Empty(emitted.OfType<ReminderRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task OffboardWithSuccessor_RequestsTheReminderOnceForTheSuccessor()
    {
        var appointment = OneOff(leadMinutes: 30);
        var fixture = new Fixture(appointment);
        var successor = Guid.NewGuid();

        await fixture.RunAsync(typeof(CalendarUserOffboardedConsumer), Offboarded(successor));

        var emitted = await fixture.EmittedAsync();
        Assert.Equal(successor, Assert.Single(emitted.OfType<ReminderRequestedIntegrationEvent>()).UserId);
        Assert.Empty(emitted.OfType<AppointmentCancelledIntegrationEvent>());
        Assert.Empty(emitted.OfType<ReminderTargetClosedIntegrationEvent>());
    }

    /// <summary>Un pedido rechazado no deja nada a medias: ni la cita movida ni un aviso de un cambio que no fue.</summary>
    [Fact]
    public async Task RescheduleRejectedForLead_RaisesNothing()
    {
        var appointment = OneOff(leadMinutes: 30);
        var fixture = new Fixture(appointment);
        var command = MoveOneOff(appointment, StartUtc.AddHours(5)) with
        {
            ReminderLeadMinutes = Appointment.MaxReminderLeadMinutes + 1,
        };

        await fixture.RunAsync(typeof(RescheduleAppointmentHandler), command, expectSuccess: false);

        Assert.Empty(appointment.DomainEvents);
        Assert.Equal(StartUtc, appointment.Timing.StartUtc);
    }

    private static DateTime TruncatedToSeconds(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private static DateTime UtcOf(DateOnly date) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TenAm), TimeZoneInfo.FindSystemTimeZoneById(NewYork)),
            DateTimeKind.Utc
        );

    private static Appointment OneOff(int? leadMinutes)
    {
        var appointment = PointInTime(Tenant, Organizer, StartUtc, leadMinutes);
        appointment.AddAttendee(
            AttendeeKind.InternalUser,
            Guest,
            null,
            AttendeeSnapshot.Create("Guest", "guest@firm.test").Value,
            false,
            Organizer,
            DateTime.UtcNow,
            false
        );
        return appointment;
    }

    private static ScheduleAppointmentCommand ScheduleCommand(int? leadMinutes) =>
        new(
            Tenant,
            Organizer,
            "Client review",
            null,
            null,
            Guid.NewGuid(),
            NewYork,
            StartUtc,
            StartUtc.AddHours(1),
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            leadMinutes
        );

    private static RescheduleAppointmentCommand MoveOneOff(Appointment appointment, DateTime newStartUtc) =>
        new(
            Tenant,
            appointment.Id,
            Organizer,
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

    private static RescheduleAppointmentCommand MoveSeries(Appointment series, DateOnly newFirstDate) =>
        new(
            Tenant,
            series.Id,
            Organizer,
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

    private static UserOffboardedIntegrationEvent Offboarded(Guid? successor) =>
        new()
        {
            TenantId = Tenant,
            UserId = Organizer,
            Email = "leaver@example.com",
            ActorType = "TenantEmployee",
            SuccessorUserId = successor,
            RemovedAtUtc = DateTime.UtcNow,
        };

    private sealed class Fixture
    {
        public Fixture(params Appointment[] seed)
        {
            Appointments = new InMemoryAppointments(seed);
        }

        public InMemoryAppointments Appointments { get; }

        public FakeMessageBus Bus { get; } = new();

        public async Task<IReadOnlyList<object>> EmittedAsync()
        {
            Assert.Empty(Bus.Published.OfType<IDomainEvent>());
            return [.. Bus.Published, .. await CalendarDomainEventReplay.DispatchAsync(Appointments.All, Appointments)];
        }

        public async Task RunAsync(Type handler, object command, bool expectSuccess = true)
        {
            var type = AppointmentType
                .Create(Tenant, "Review", TimeSpan.FromHours(1), "#336699", DateTime.UtcNow)
                .Value;
            var services = new List<object>
            {
                Appointments,
                Bus,
                new CountingUnitOfWork(),
                new FixedCorrelation(),
                new NullCalendarMetrics(),
                new SingleTypeRepository(type),
                new EmptyAvailability(),
                new NoFeedTokens(),
                new NoFeedCache(),
            };
            var method = handler
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Handle" && m.GetParameters()[0].ParameterType == command.GetType());
            var args = method
                .GetParameters()
                .Select(p =>
                    p.ParameterType == command.GetType() ? command
                    : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                    : IsLogger(p.ParameterType)
                        ? Activator.CreateInstance(
                            typeof(NullLogger<>).MakeGenericType(p.ParameterType.GetGenericArguments())
                        )
                    : services.First(s => p.ParameterType.IsInstanceOfType(s))
                )
                .ToArray();

            var task = (Task)method.Invoke(null, args)!;
            await task;
            if (expectSuccess && task.GetType().GetProperty("Result")?.GetValue(task) is Result result)
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        private static bool IsLogger(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>);
    }
}
