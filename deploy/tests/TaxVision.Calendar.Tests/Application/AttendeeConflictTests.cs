using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Los choques miran también la agenda de los invitados del equipo. Antes solo la del organizador:
/// invitar a Andres a las 10 cuando ya tenía otra cita a las 10 no avisaba a nadie.
/// </summary>
public sealed class AttendeeConflictTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _carlos = Guid.NewGuid();
    private readonly Guid _olga = Guid.NewGuid();
    private readonly Guid _andres = Guid.NewGuid();
    private readonly DateTime _start = DateTime.UtcNow.Date.AddDays(3).AddHours(15);

    [Fact]
    public async Task Inviting_someone_who_is_busy_then_warns_with_their_name()
    {
        var appointments = new InMemoryAppointments(Existing(organizer: _olga, attendee: _andres));

        var result = await Schedule(appointments, Type(blocks: false), withAndres: true);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Andres Hernandez"));
    }

    [Fact]
    public async Task A_type_that_does_not_allow_overlaps_also_blocks_for_the_invitee()
    {
        var appointments = new InMemoryAppointments(Existing(organizer: _andres, attendee: null));

        var result = await Schedule(appointments, Type(blocks: true), withAndres: true);

        Assert.Equal("Calendar.Appointment.AttendeeConflict", result.Error.Code);
        Assert.Contains("Andres Hernandez", result.Error.Message);
    }

    [Fact]
    public async Task An_appointment_the_invitee_declined_does_not_count()
    {
        var other = Existing(organizer: _olga, attendee: _andres);
        other.RespondAsAttendee(AttendeeResponse.Declined, _andres, null, null, DateTime.UtcNow);
        var appointments = new InMemoryAppointments(other);

        var result = await Schedule(appointments, Type(blocks: false), withAndres: true);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public async Task The_organizer_is_busy_also_where_they_are_only_a_guest()
    {
        var appointments = new InMemoryAppointments(Existing(organizer: _olga, attendee: _carlos));

        var result = await Schedule(appointments, Type(blocks: false), withAndres: false);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Warnings);
    }

    [Fact]
    public async Task Time_off_of_an_invitee_warns_but_does_not_block()
    {
        var appointments = new InMemoryAppointments();
        var availability = new BlocksFor(
            BlockedTime
                .Create(_tenant, _andres, _start.AddHours(-1), _start.AddHours(3), "Vacation", DateTime.UtcNow)
                .Value
        );

        var result = await Schedule(appointments, Type(blocks: true), withAndres: true, availability);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Andres Hernandez") && w.Contains("time off"));
    }

    [Fact]
    public async Task Adding_a_busy_person_to_an_existing_appointment_warns()
    {
        var mine = Existing(organizer: _carlos, attendee: null);
        var appointments = new InMemoryAppointments(mine, Existing(organizer: _olga, attendee: _andres));
        var type = Type(blocks: false);

        var result = await AddAttendeeHandler.Handle(
            new AddAttendeeCommand(
                _tenant,
                mine.Id,
                _carlos,
                AttendeeKind.InternalUser,
                _andres,
                null,
                "Andres Hernandez",
                "andres@office.com",
                true,
                false
            ),
            appointments,
            new SingleTypeRepository(type),
            new EmptyAvailability(),
            new CountingUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Andres Hernandez"));
    }

    private Appointment Existing(Guid organizer, Guid? attendee)
    {
        var appointment = Appointment
            .Schedule(
                _tenant,
                AppointmentTitle.Create("Other").Value,
                EventTiming.PointInTimeOf(_start, _start.AddMinutes(30), "America/New_York").Value,
                Guid.NewGuid(),
                organizer,
                DateTime.UtcNow
            )
            .Value;
        if (attendee is { } userId)
            appointment.AddAttendee(
                AttendeeKind.InternalUser,
                userId,
                null,
                AttendeeSnapshot.Create("Guest", "guest@office.com").Value,
                isRequired: true,
                organizer,
                DateTime.UtcNow,
                canManageAll: false
            );
        return appointment;
    }

    private AppointmentType Type(bool blocks) =>
        AppointmentType
            .Create(_tenant, "Review", TimeSpan.FromMinutes(30), "#336699", DateTime.UtcNow, blocksOnConflict: blocks)
            .Value;

    private Task<Result<AppointmentWithWarnings>> Schedule(
        InMemoryAppointments appointments,
        AppointmentType type,
        bool withAndres,
        IAvailabilityRepository? availability = null
    ) =>
        ScheduleAppointmentHandler.Handle(
            new ScheduleAppointmentCommand(
                _tenant,
                _carlos,
                "Consulta inicial",
                null,
                null,
                type.Id,
                "America/New_York",
                _start,
                _start.AddMinutes(30),
                null,
                null,
                null,
                null,
                null,
                null,
                false
            )
            {
                Attendees = withAndres
                    ?
                    [
                        new ScheduleAttendeeInput(
                            AttendeeKind.InternalUser,
                            _andres,
                            null,
                            "Andres Hernandez",
                            "andres@office.com",
                            true
                        ),
                    ]
                    : [],
            },
            appointments,
            new SingleTypeRepository(type),
            availability ?? new EmptyAvailability(),
            new NoScheduleLock(),
            new CountingUnitOfWork(),
            new NullCalendarMetrics(),
            CancellationToken.None
        );

    /// <summary>Bloqueos de agenda fijos, de quien sea.</summary>
    private sealed class BlocksFor(params BlockedTime[] blocks) : IAvailabilityRepository
    {
        public Task<IReadOnlyList<AvailabilityRule>> ListRulesAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<AvailabilityRule>>([]);

        public Task<IReadOnlyList<BlockedTime>> ListBlocksAsync(
            Guid tenantId,
            Guid userId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<BlockedTime>>([.. blocks.Where(b => b.UserId == userId)]);

        public Task<IReadOnlyList<BlockedTime>> ListAllBlocksForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<BlockedTime>>([]);

        public Task<BlockedTime?> FindBlockAsync(Guid tenantId, Guid blockId, CancellationToken ct = default) =>
            Task.FromResult<BlockedTime?>(null);

        public void AddRule(AvailabilityRule rule) { }

        public void AddBlock(BlockedTime block) { }

        public void RemoveBlock(BlockedTime block) { }
    }
}
