using BuildingBlocks.CustomerVisibility;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>"Mi agenda" en SQL: el filtro por participante se compone con el recorte por asignación, sin perderlo.</summary>
public sealed class AppointmentParticipantRangeIntegrationTests : IAsyncLifetime
{
    private const string NewYork = "America/New_York";
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime From = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _someoneElse = Guid.NewGuid();
    private readonly Guid _unassignedCustomer = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Participant_agenda_keeps_the_assignment_scope()
    {
        var attendsUnassigned = OneOff(_someoneElse, _unassignedCustomer);
        AddAttendee(attendsUnassigned, _user);
        var organizes = OneOff(_user, _unassignedCustomer);
        var othersSeries = Series(_someoneElse);
        var ownSeries = Series(_user);
        await SaveAsync(attendsUnassigned, organizes, othersSeries, ownSeries);

        var scoped = await ListAsync(assignedTo: _user);
        var unscoped = await ListAsync(assignedTo: null);

        Assert.Equal(new[] { organizes.Id, ownSeries.Id }.Order(), scoped.Order());
        Assert.Equal(new[] { attendsUnassigned.Id, organizes.Id, ownSeries.Id }.Order(), unscoped.Order());
    }

    [Fact]
    public async Task An_assigned_client_appointment_where_the_user_attends_is_visible()
    {
        var assignedCustomer = Guid.NewGuid();
        var attends = OneOff(_someoneElse, assignedCustomer);
        AddAttendee(attends, _user);
        await SaveAsync(attends);
        await using (var db = _database.NewContext())
        {
            db.Set<CustomerAssignmentProjection>()
                .Add(CustomerAssignmentProjection.Create(_tenantId, assignedCustomer, _user, Now));
            await db.SaveChangesAsync();
        }

        Assert.Equal([attends.Id], await ListAsync(assignedTo: _user));
    }

    private async Task<List<Guid>> ListAsync(Guid? assignedTo)
    {
        await using var db = _database.NewContext();
        var rows = await new AppointmentRepository(db).ListVisibleForUserRangeAsync(
            _tenantId,
            _user,
            From,
            From.AddDays(1),
            assignedTo
        );
        return rows.Select(a => a.Id).ToList();
    }

    private Appointment OneOff(Guid organizer, Guid? customerId)
    {
        var start = From.AddHours(15);
        var timing = EventTiming.PointInTimeOf(start, start.AddHours(1), NewYork).Value;
        return Appointment
            .Schedule(
                _tenantId,
                AppointmentTitle.Create("Client review").Value,
                timing,
                Guid.NewGuid(),
                organizer,
                Now,
                customerId: customerId
            )
            .Value;
    }

    private Appointment Series(Guid organizer)
    {
        var appointment = OneOff(organizer, customerId: null);
        var series = EventTiming
            .RecurringOf(new DateOnly(2026, 11, 2), new TimeOnly(9, 0), TimeSpan.FromHours(1), NewYork)
            .Value;
        Assert.True(
            appointment.MakeRecurring(RecurrenceRule.Create("FREQ=DAILY").Value, series, organizer, false).IsSuccess
        );
        return appointment;
    }

    private static void AddAttendee(Appointment appointment, Guid userId) =>
        Assert.True(
            appointment
                .AddAttendee(
                    AttendeeKind.InternalUser,
                    userId,
                    null,
                    AttendeeSnapshot.Create("Ana", "ana@example.com").Value,
                    isRequired: true,
                    appointment.OrganizerUserId,
                    Now,
                    canManageAll: false
                )
                .IsSuccess
        );

    private async Task SaveAsync(params Appointment[] appointments)
    {
        await using var db = _database.NewContext();
        db.Appointments.AddRange(appointments);
        await db.SaveChangesAsync();
    }
}
