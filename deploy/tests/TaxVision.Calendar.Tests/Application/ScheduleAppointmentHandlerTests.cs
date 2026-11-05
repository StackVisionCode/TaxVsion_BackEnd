using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.Types;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>El aviso antes de la cita se pide al crearla, sin esperar al job de 12 h.</summary>
public sealed class ScheduleAppointmentHandlerTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();

    [Fact]
    public async Task WithReminderLead_RequestsRemindersImmediately()
    {
        var start = TruncateToSeconds(DateTime.UtcNow.AddHours(3));
        var appointments = new InMemoryAppointments();
        var unitOfWork = new CountingUnitOfWork();
        var bus = new FakeMessageBus();
        var publishedBeforeSave = 0;
        bus.OnPublish = () => publishedBeforeSave += unitOfWork.Saves == 0 ? 1 : 0;

        var result = await Schedule(Command(start, reminderLeadMinutes: 30), appointments, unitOfWork, bus);

        Assert.True(result.IsSuccess);
        var appointment = Assert.Single(appointments.All);
        Assert.Equal(30, appointment.ReminderLeadMinutes);
        var request = Assert.Single(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
        var target = OccurrenceTargetId.For(appointment.Id, start);
        Assert.Equal(target, request.TargetId);
        Assert.Equal(AppointmentReminderRequests.RequestKey(target, 0), request.RequestKey);
        Assert.Equal(30, request.LeadMinutes);
        Assert.Equal(_organizer, request.UserId);
        Assert.Equal(0, publishedBeforeSave);
    }

    [Fact]
    public async Task WithoutReminderLead_RequestsNothing()
    {
        var bus = new FakeMessageBus();

        var result = await Schedule(
            Command(TruncateToSeconds(DateTime.UtcNow.AddHours(3)), reminderLeadMinutes: null),
            new InMemoryAppointments(),
            new CountingUnitOfWork(),
            bus
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Published.OfType<ReminderRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task WithReminderLeadAboveMaximum_FailsWithoutSaving()
    {
        var unitOfWork = new CountingUnitOfWork();

        var result = await Schedule(
            Command(TruncateToSeconds(DateTime.UtcNow.AddHours(3)), reminderLeadMinutes: int.MaxValue),
            new InMemoryAppointments(),
            unitOfWork,
            new FakeMessageBus()
        );

        Assert.True(result.IsFailure);
        Assert.Equal(0, unitOfWork.Saves);
    }

    private static DateTime TruncateToSeconds(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private ScheduleAppointmentCommand Command(DateTime startUtc, int? reminderLeadMinutes) =>
        new(
            _tenant,
            _organizer,
            "Client review",
            null,
            null,
            Guid.NewGuid(),
            "America/New_York",
            startUtc,
            startUtc.AddHours(1),
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            reminderLeadMinutes
        );

    private Task<BuildingBlocks.Results.Result<AppointmentWithWarnings>> Schedule(
        ScheduleAppointmentCommand command,
        InMemoryAppointments appointments,
        CountingUnitOfWork unitOfWork,
        FakeMessageBus bus
    )
    {
        var type = AppointmentType.Create(_tenant, "Review", TimeSpan.FromHours(1), "#336699", DateTime.UtcNow).Value;
        return ScheduleAppointmentHandler.Handle(
            command,
            appointments,
            new SingleTypeRepository(type),
            new EmptyAvailability(),
            unitOfWork,
            bus,
            new FixedCorrelation(),
            new NullCalendarMetrics(),
            CancellationToken.None
        );
    }
}
