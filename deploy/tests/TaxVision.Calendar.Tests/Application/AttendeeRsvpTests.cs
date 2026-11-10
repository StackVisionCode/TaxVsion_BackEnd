using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.DomainEvents;
using TaxVision.Calendar.Application.Appointments.Rsvp;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using TaxVision.Calendar.Domain.ValueObjects;
using Wolverine;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// La respuesta desde el correo: el enlace de cada invitado responde por él sin sesión. Antes el .ics
/// pedía RSVP y la respuesta de Gmail o Proton iba a un no-reply que nadie leía. La cita va en 2030:
/// el handler usa la hora real y una cita pasada ya no acepta respuestas.
/// </summary>
public sealed class AttendeeRsvpTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static (Appointment Appointment, string Token) WithExternal()
    {
        var appointment = Appointment
            .Schedule(
                Tenant,
                AppointmentTitle.Create("Revision de la declaracion").Value,
                EventTiming
                    .PointInTimeOf(
                        new DateTime(2030, 3, 10, 14, 0, 0, DateTimeKind.Utc),
                        new DateTime(2030, 3, 10, 15, 0, 0, DateTimeKind.Utc),
                        "America/New_York"
                    )
                    .Value,
                Guid.NewGuid(),
                Organizer,
                Now
            )
            .Value;
        appointment.AddAttendee(
            AttendeeKind.External,
            null,
            null,
            AttendeeSnapshot.Create("Manuel", "manuel@cliente.test").Value,
            isRequired: false,
            Organizer,
            Now,
            canManageAll: false
        );
        var token = ((AppointmentAttendeeInvitedDomainEvent)appointment.DomainEvents[^1]).RsvpToken!;
        appointment.ClearDomainEvents();
        return (appointment, token);
    }

    [Fact]
    public async Task The_link_shows_the_appointment_and_records_the_answer()
    {
        var (appointment, token) = WithExternal();
        var lookup = new RsvpLookup(appointment);
        var unitOfWork = new CountingUnitOfWork();

        var shown = await GetAppointmentRsvpHandler.Handle(
            new GetAppointmentRsvpQuery(token),
            lookup,
            new Hosts(),
            default
        );
        var answered = await RespondByRsvpHandler.Handle(
            new RespondByRsvpCommand(token, AttendeeResponse.Declined),
            lookup,
            new Hosts(),
            unitOfWork,
            default
        );

        Assert.True(shown.IsSuccess);
        Assert.Equal("Revision de la declaracion", shown.Value.Title);
        Assert.Equal("Manuel", shown.Value.AttendeeName);
        // La página pública pinta el logo de la oficina con esta clave, aunque se abra sin su subdominio.
        Assert.Equal("manfer.taxoffix.io", shown.Value.BrandingKey);
        Assert.Equal("NeedsAction", shown.Value.Response);
        Assert.True(answered.IsSuccess);
        Assert.Equal("Declined", answered.Value.Response);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Single(appointment.DomainEvents);
    }

    [Fact]
    public async Task An_unknown_link_says_not_found_and_only_real_answers_are_accepted()
    {
        var (appointment, token) = WithExternal();
        var lookup = new RsvpLookup(appointment);
        var unitOfWork = new CountingUnitOfWork();

        var unknown = await GetAppointmentRsvpHandler.Handle(
            new GetAppointmentRsvpQuery("nope"),
            lookup,
            new Hosts(),
            default
        );
        var reset = await RespondByRsvpHandler.Handle(
            new RespondByRsvpCommand(token, AttendeeResponse.NeedsAction),
            lookup,
            new Hosts(),
            unitOfWork,
            default
        );

        Assert.Equal(AppointmentErrors.NotFound, unknown.Error);
        Assert.True(reset.IsFailure);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task The_added_and_moved_notices_carry_each_attendee_link()
    {
        var (appointment, _) = WithExternal();
        appointment.AddAttendee(
            AttendeeKind.External,
            null,
            null,
            AttendeeSnapshot.Create("Lucia", "lucia@cliente.test").Value,
            isRequired: false,
            Organizer,
            Now,
            canManageAll: false
        );
        var invited = (AppointmentAttendeeInvitedDomainEvent)appointment.DomainEvents[^1];
        appointment.Reschedule(
            EventTiming
                .PointInTimeOf(
                    new DateTime(2030, 3, 11, 14, 0, 0, DateTimeKind.Utc),
                    new DateTime(2030, 3, 11, 15, 0, 0, DateTimeKind.Utc),
                    "America/New_York"
                )
                .Value,
            Organizer,
            canManageAll: false,
            Now
        );
        var moved = (AppointmentRescheduledDomainEvent)appointment.DomainEvents[^1];
        var appointments = new InMemoryAppointments(appointment);

        var added = await AppointmentAttendeeInvitedDomainEventHandler.Handle(
            invited,
            appointments,
            new Envelope(),
            default
        );
        var rescheduled = await AppointmentRescheduledDomainEventHandler.Handle(
            moved,
            appointments,
            new Envelope(),
            default
        );

        Assert.Equal(invited.RsvpToken, added!.RsvpToken);
        Assert.All(
            rescheduled!.Recipients,
            r => Assert.Equal(r.Email, appointment.FindByRsvpToken(r.RsvpToken!)?.Snapshot.Email)
        );
    }

    private sealed class Hosts : TaxVision.Calendar.Application.Staff.Abstractions.ITenantHostResolver
    {
        public Task<string?> ResolveHostAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("manfer.taxoffix.io");
    }

    private sealed class RsvpLookup(Appointment appointment) : IAppointmentRsvpLookup
    {
        public Task<Appointment?> FindByRsvpTokenAsync(string token, CancellationToken ct = default) =>
            Task.FromResult(appointment.FindByRsvpToken(token) is null ? null : appointment);
    }
}
