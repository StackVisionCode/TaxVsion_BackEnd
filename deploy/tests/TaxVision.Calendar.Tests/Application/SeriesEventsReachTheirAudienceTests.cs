using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using Wolverine;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Quién se entera de qué. Los dos casos de abajo salían mudos: el evento que se publicaba —el hecho
/// preciso del dominio— no lo escuchaba nadie, y el que Notification y Reminder sí escuchan no salía.
/// Ningún test lo veía porque los tres publicaban <b>algo</b>.
/// </summary>
public sealed class SeriesEventsReachTheirAudienceTests
{
    private const string NewYork = "America/New_York";

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Lunes 9 de marzo de 2026, 9:00 en Nueva York, ya en horario de verano.</summary>
    private static readonly DateTime SecondOccurrence = new(2026, 3, 9, 13, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Cancelar una de las ocurrencias tiene que avisarle a los asistentes. Es el aviso que más
    /// importa: no mandarlo deja a alguien presentándose a una reunión que ya no existe.
    /// </summary>
    [Fact]
    public async Task Cancelling_one_occurrence_still_tells_the_attendees()
    {
        var series = SeriesWithAttendee();
        var bus = new FakeMessageBus();

        var result = await CancelAppointmentHandler.Handle(
            new CancelAppointmentCommand(
                _tenant,
                series.Id,
                _organizer,
                EditScope.ThisOccurrence,
                SecondOccurrence,
                "feriado"
            ),
            new SingleAppointmentRepository(series),
            new NoOpUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            new NoOpMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);

        var cancelled = Assert.Single(bus.Published.OfType<AppointmentCancelledIntegrationEvent>());
        Assert.Equal(nameof(EditScope.ThisOccurrence), cancelled.Scope);
        Assert.Equal(SecondOccurrence, cancelled.OriginalStartUtc);
        Assert.Contains(cancelled.Recipients, r => r.Email == "cliente@example.com");

        // Y el hecho preciso sigue saliendo: dice cuál de las N ocurrencias se cayó.
        Assert.Single(bus.Published.OfType<OccurrenceCancelledIntegrationEvent>());
    }

    /// <summary>
    /// Partir una serie deja los avisos del otro lado del corte apuntando a una serie que ya no llega
    /// hasta ahí. Sin cerrarlos, el viejo suena a la hora vieja y el de la serie nueva también: dos
    /// avisos para la misma reunión.
    /// </summary>
    [Fact]
    public async Task Splitting_a_series_closes_the_orphaned_reminders_and_tells_the_attendees()
    {
        var series = SeriesWithAttendee();
        var bus = new FakeMessageBus();

        var result = await RescheduleAppointmentHandler.Handle(
            new RescheduleAppointmentCommand(
                _tenant,
                series.Id,
                _organizer,
                EditScope.ThisAndFollowing,
                SecondOccurrence,
                null,
                null,
                new DateOnly(2026, 3, 9),
                new TimeOnly(11, 0),
                TimeSpan.FromHours(1),
                NewYork,
                "FREQ=WEEKLY;BYDAY=MO"
            ),
            new SingleAppointmentRepository(series),
            new NoOpUnitOfWork(),
            bus,
            new NoOpCorrelationContext(),
            new NoOpMetrics(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(bus.Published.OfType<CalendarSeriesSplitIntegrationEvent>());

        // Un cierre por cada ocurrencia que la serie vieja tenía del corte en adelante.
        var closed = bus.Published.OfType<ReminderTargetClosedIntegrationEvent>().ToList();
        Assert.NotEmpty(closed);
        Assert.All(closed, c => Assert.Equal("Calendar", c.Category));

        var moved = Assert.Single(bus.Published.OfType<AppointmentRescheduledIntegrationEvent>());
        Assert.Equal(nameof(EditScope.ThisAndFollowing), moved.Scope);
        Assert.Contains(moved.Recipients, r => r.Email == "cliente@example.com");
    }

    private Appointment SeriesWithAttendee()
    {
        var timing = EventTiming
            .RecurringOf(new DateOnly(2026, 3, 2), new TimeOnly(9, 0), TimeSpan.FromHours(1), NewYork)
            .Value;

        var series = Appointment
            .Schedule(
                _tenant,
                AppointmentTitle.Create("Revision semanal").Value,
                timing,
                Guid.NewGuid(),
                _organizer,
                Now
            )
            .Value;

        series.MakeRecurring(RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value, timing, _organizer);
        series.AddAttendee(
            AttendeeKind.Customer,
            null,
            Guid.NewGuid(),
            AttendeeSnapshot.Create("Amanda", "cliente@example.com").Value,
            isRequired: true,
            _organizer,
            Now
        );

        return series;
    }

    private sealed class SingleAppointmentRepository(Appointment appointment) : IAppointmentRepository
    {
        public Task<Result<Appointment>> GetByIdAsync(
            Guid tenantId,
            Guid appointmentId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                appointmentId == appointment.Id
                    ? Result.Success(appointment)
                    : Result.Failure<Appointment>(AppointmentErrors.NotFound)
            );

        public Task<IReadOnlyList<Appointment>> ListForRangeAsync(
            Guid tenantId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>([appointment]);

        public Task<IReadOnlyList<Appointment>> ListForUserRangeAsync(
            Guid tenantId,
            Guid userId,
            DateTime rangeStartUtc,
            DateTime rangeEndUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Appointment>>([appointment]);

        public void Add(Appointment created) { }

        public void Remove(Appointment removed) { }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpMetrics : ICalendarMetrics
    {
        public void RecordCreated(bool isRecurring) { }

        public void RecordRescheduled(bool isRecurring) { }

        public void RecordCancelled(bool isRecurring) { }

        public void RecordExpansionDuration(double milliseconds, int seriesCount) { }

        public void RecordConflictDetected(bool blocked) { }

        public void RecordIcsFeedRequest(bool found) { }

        public void RecordIcsFeedStale() { }
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
