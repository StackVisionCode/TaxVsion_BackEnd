using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Appointments.DomainEvents;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using TaxVision.Calendar.Domain.Types;
using Wolverine;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

public sealed class ScheduleAppointmentAttendeesTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private readonly Guid _andres = Guid.NewGuid();

    [Fact]
    public async Task People_chosen_when_booking_join_with_the_organizer_already_accepted()
    {
        var appointments = new InMemoryAppointments();

        var result = await Schedule(
            appointments,
            new OrganizerContact("Carlos Castillo", "carlos@office.com"),
            [
                new ScheduleAttendeeInput(
                    AttendeeKind.InternalUser,
                    _andres,
                    null,
                    "Andres Hernandez",
                    "andres@office.com",
                    true
                ),
                new ScheduleAttendeeInput(AttendeeKind.External, null, null, "Mia Lopez", "mia@partner.com", false),
            ]
        );

        Assert.True(result.IsSuccess);
        var appointment = Assert.Single(appointments.All);
        Assert.Equal(3, appointment.Attendees.Count);
        var organizer = appointment.Attendees.Single(a => a.UserId == _organizer);
        Assert.Equal(AttendeeResponse.Accepted, organizer.Response);
        Assert.Equal(AttendeeResponse.NeedsAction, appointment.Attendees.Single(a => a.UserId == _andres).Response);
        // Llegan con el alta: ningún "te agregaron" aparte.
        Assert.DoesNotContain(appointment.DomainEvents, e => e is AppointmentAttendeeInvitedDomainEvent);

        var outgoing = await AppointmentScheduledDomainEventHandler.Handle(
            appointment.DomainEvents.OfType<AppointmentScheduledDomainEvent>().Single(),
            appointments,
            new Envelope(),
            CancellationToken.None
        );
        var scheduled = outgoing.OfType<AppointmentScheduledIntegrationEvent>().Single();
        Assert.Equal(
            ["carlos@office.com", "andres@office.com", "mia@partner.com"],
            scheduled.Recipients.Select(r => r.Email)
        );
        // Enlace de respuesta solo para quien no respondió (el organizador ya aceptó), y es el de su propio hash.
        Assert.Null(scheduled.Recipients.Single(r => r.Email == "carlos@office.com").RsvpToken);
        foreach (var recipient in scheduled.Recipients.Where(r => r.Email != "carlos@office.com"))
        {
            var attendee = appointment.FindByRsvpToken(recipient.RsvpToken!);
            Assert.Equal(recipient.Email, attendee?.Snapshot.Email);
        }
    }

    [Fact]
    public async Task The_organizer_listed_again_is_not_duplicated_and_without_email_is_left_out()
    {
        var appointments = new InMemoryAppointments();

        await Schedule(
            appointments,
            new OrganizerContact("Carlos Castillo", null),
            [
                new ScheduleAttendeeInput(
                    AttendeeKind.InternalUser,
                    _organizer,
                    null,
                    "Carlos",
                    "carlos@office.com",
                    true
                ),
            ]
        );

        var appointment = Assert.Single(appointments.All);
        var only = Assert.Single(appointment.Attendees);
        Assert.Equal(_organizer, only.UserId);
        Assert.Equal(AttendeeResponse.Accepted, only.Response);
    }

    /// <summary>
    /// Un empleado y un cliente con el mismo correo son dos personas: se identifican por su id. Un
    /// externo con el correo de alguien que ya está sí es el mismo.
    /// </summary>
    [Fact]
    public async Task A_shared_email_does_not_merge_a_team_member_with_a_client_but_does_merge_an_external()
    {
        var appointments = new InMemoryAppointments();
        var client = Guid.NewGuid();

        await Schedule(
            appointments,
            null,
            [
                new ScheduleAttendeeInput(
                    AttendeeKind.Customer,
                    null,
                    client,
                    "Minato Kenyimura",
                    "shared@example.com",
                    true
                ),
                new ScheduleAttendeeInput(
                    AttendeeKind.InternalUser,
                    _andres,
                    null,
                    "Andres Hernandez",
                    "shared@example.com",
                    true
                ),
                new ScheduleAttendeeInput(
                    AttendeeKind.External,
                    null,
                    null,
                    "Same mailbox",
                    "SHARED@example.com",
                    false
                ),
            ]
        );

        var appointment = Assert.Single(appointments.All);
        Assert.Equal(2, appointment.Attendees.Count);
        Assert.Contains(appointment.Attendees, a => a.CustomerId == client);
        Assert.Contains(appointment.Attendees, a => a.UserId == _andres);
    }

    [Fact]
    public async Task An_invalid_attendee_rejects_the_booking()
    {
        var appointments = new InMemoryAppointments();

        var result = await Schedule(
            appointments,
            null,
            [new ScheduleAttendeeInput(AttendeeKind.External, null, null, "Mia", "not-an-email", false)]
        );

        Assert.Equal(AppointmentErrors.AttendeeEmailInvalid, result.Error);
        Assert.Empty(appointments.All);
    }

    private Task<Result<AppointmentWithWarnings>> Schedule(
        InMemoryAppointments appointments,
        OrganizerContact? organizer,
        IReadOnlyList<ScheduleAttendeeInput> attendees
    )
    {
        var type = AppointmentType
            .Create(_tenant, "Review", TimeSpan.FromMinutes(30), "#336699", DateTime.UtcNow)
            .Value;
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(15);
        return ScheduleAppointmentHandler.Handle(
            new ScheduleAppointmentCommand(
                _tenant,
                _organizer,
                "Consulta inicial",
                null,
                null,
                type.Id,
                "America/New_York",
                start,
                start.AddMinutes(30),
                null,
                null,
                null,
                null,
                null,
                null,
                true
            )
            {
                Organizer = organizer,
                Attendees = attendees,
            },
            appointments,
            new SingleTypeRepository(type),
            new EmptyAvailability(),
            new NoScheduleLock(),
            new CountingUnitOfWork(),
            new NullCalendarMetrics(),
            CancellationToken.None
        );
    }
}
