using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Appointments.DomainEvents;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using TaxVision.Calendar.Domain.ValueObjects;
using Wolverine;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>"Edit details": título, descripción y lugar de una cita ya agendada, con aviso a los invitados.</summary>
public sealed class EditAppointmentDetailsTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private readonly Guid _andres = Guid.NewGuid();

    private Appointment Booked()
    {
        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(15);
        var appointment = Appointment
            .Schedule(
                _tenant,
                AppointmentTitle.Create("Consulta inicial").Value,
                EventTiming.PointInTimeOf(start, start.AddMinutes(30), "America/New_York").Value,
                Guid.NewGuid(),
                _organizer,
                DateTime.UtcNow
            )
            .Value;
        appointment.AddAttendee(
            AttendeeKind.InternalUser,
            _andres,
            null,
            AttendeeSnapshot.Create("Andres Hernandez", "andres@office.com").Value,
            isRequired: true,
            _organizer,
            DateTime.UtcNow,
            canManageAll: false
        );
        return appointment;
    }

    [Fact]
    public async Task The_organizer_saves_new_details_and_attendees_get_the_change()
    {
        var appointment = Booked();
        var appointments = new InMemoryAppointments(appointment);
        var unitOfWork = new CountingUnitOfWork();

        var result = await EditAppointmentDetailsHandler.Handle(
            new EditAppointmentDetailsCommand(
                _tenant,
                appointment.Id,
                _organizer,
                "Revisión final",
                "Bring your W-2",
                "Office 2",
                false
            ),
            appointments,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Revisión final", result.Value.Title);
        Assert.Equal("Office 2", result.Value.Location);
        Assert.Equal(1, unitOfWork.Saves);

        var changed = appointment.DomainEvents.OfType<AppointmentDetailsChangedDomainEvent>().Single();
        var outgoing = await AppointmentDetailsChangedDomainEventHandler.Handle(
            changed,
            appointments,
            new Envelope(),
            default
        );

        Assert.Equal("Consulta inicial", outgoing!.PreviousTitle);
        Assert.Equal("Revisión final", outgoing.Title);
        Assert.Equal(["Title", "Description", "Location"], outgoing.ChangedFields);
        Assert.Equal(_organizer, outgoing.ChangedByUserId);
        Assert.Contains(outgoing.Recipients, r => r.UserId == _andres);
        // Ningún enlace de respuesta: cambiar el nombre no pide volver a responder.
        Assert.All(outgoing.Recipients, r => Assert.Null(r.RsvpToken));
    }

    [Fact]
    public async Task An_empty_title_is_rejected_without_saving()
    {
        var appointment = Booked();
        var unitOfWork = new CountingUnitOfWork();

        var result = await EditAppointmentDetailsHandler.Handle(
            new EditAppointmentDetailsCommand(_tenant, appointment.Id, _organizer, "  ", null, null, false),
            new InMemoryAppointments(appointment),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(0, unitOfWork.Saves);
    }
}
