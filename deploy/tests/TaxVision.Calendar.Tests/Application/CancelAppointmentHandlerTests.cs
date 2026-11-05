using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using Xunit;
using static TaxVision.Calendar.Tests.Application.ReminderTargetsTestData;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>Cancelar tiene que cerrar en Reminder exactamente los objetivos que se habían pedido.</summary>
public sealed class CancelAppointmentHandlerTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();

    [Fact]
    public async Task CancelEntireWeeklySeries_ClosesEveryOccurrenceInHorizon()
    {
        var monday = Next(DayOfWeek.Monday);
        var series = Weekly(_tenant, _organizer, monday, leadMinutes: 30);
        var expected = WeeklyTargets(series.Id, monday);
        var bus = new FakeMessageBus();

        var result = await Cancel(series, bus, EditScope.EntireSeries, originalStartUtc: null);

        Assert.True(result.IsSuccess);
        Assert.InRange(expected.Count, 8, 9);
        var closed = bus.Published.OfType<ReminderTargetClosedIntegrationEvent>().Select(c => c.TargetId).ToList();
        Assert.Equal(expected.Count, closed.Count);
        Assert.Equal(expected, closed.ToHashSet());
    }

    [Fact]
    public async Task CancelOneOff_ClosesItsOwnTarget()
    {
        var start = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(3).AddHours(14), DateTimeKind.Utc);
        var appointment = PointInTime(_tenant, _organizer, start, leadMinutes: 30);
        var bus = new FakeMessageBus();

        var result = await Cancel(appointment, bus, scope: null, originalStartUtc: null);

        Assert.True(result.IsSuccess);
        var closed = Assert.Single(bus.Published.OfType<ReminderTargetClosedIntegrationEvent>());
        Assert.Equal(OccurrenceTargetId.For(appointment.Id, start), closed.TargetId);
    }

    private Task<BuildingBlocks.Results.Result> Cancel(
        Appointment appointment,
        FakeMessageBus bus,
        EditScope? scope,
        DateTime? originalStartUtc
    ) =>
        CancelAppointmentHandler.Handle(
            new CancelAppointmentCommand(_tenant, appointment.Id, _organizer, scope, originalStartUtc, "test", false),
            new InMemoryAppointments(appointment),
            new CountingUnitOfWork(),
            bus,
            new FixedCorrelation(),
            new NullCalendarMetrics(),
            CancellationToken.None
        );
}
