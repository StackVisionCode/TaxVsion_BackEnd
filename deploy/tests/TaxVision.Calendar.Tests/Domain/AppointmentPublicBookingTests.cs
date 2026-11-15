using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

/// <summary>La cita que nace de una página pública y los cambios que pide el invitado sin cuenta.</summary>
public sealed class AppointmentPublicBookingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Host = Guid.NewGuid();
    private static readonly Guid Page = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

    private static EventTiming At(DateTime startUtc) =>
        EventTiming.PointInTimeOf(startUtc, startUtc.AddMinutes(30), "America/New_York").Value;

    private static Appointment Demo(Guid? bookingPageId)
    {
        var visitor = new InitialAttendee(
            AttendeeKind.External,
            null,
            null,
            AttendeeSnapshot.Create("Jane Doe", "jane@acme-tax.com").Value,
            IsRequired: true,
            Accepted: true
        );
        var appointment = Appointment.Schedule(
            Tenant,
            AppointmentTitle.Create("Live demo · Acme Tax").Value,
            At(new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc)),
            Guid.NewGuid(),
            Host,
            Now,
            location: Location.Create("https://meet.google.com/abc-defg-hij").Value,
            attendees: [visitor],
            bookingPageId: bookingPageId
        );
        Assert.True(appointment.IsSuccess, appointment.IsFailure ? appointment.Error.Code : null);
        return appointment.Value;
    }

    [Fact]
    public void A_public_booking_appointment_remembers_its_page_and_is_not_virtual()
    {
        var appointment = Demo(Page);

        Assert.Equal(Page, appointment.BookingPageId);
        Assert.False(appointment.IsVirtual);
    }

    [Fact]
    public void The_external_requester_can_move_and_cancel_it()
    {
        var appointment = Demo(Page);

        Assert.True(
            appointment
                .RescheduleByExternalRequester(At(new DateTime(2026, 11, 19, 14, 0, 0, DateTimeKind.Utc)), Now)
                .IsSuccess
        );
        Assert.Equal(new DateTime(2026, 11, 19, 14, 0, 0, DateTimeKind.Utc), appointment.Timing.StartUtc);
        Assert.All(
            appointment.Attendees.Where(a => a.Kind == AttendeeKind.External),
            a => Assert.Equal(AttendeeResponse.Accepted, a.Response)
        );

        Assert.True(appointment.CancelByExternalRequester(null, Now).IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
    }

    [Fact]
    public void Moving_to_a_slot_of_another_host_hands_the_appointment_over()
    {
        var appointment = Demo(Page);
        var other = Guid.NewGuid();
        var link = Location.Create("https://zoom.us/j/123456789").Value;

        var moved = appointment.RescheduleByExternalRequester(
            At(new DateTime(2026, 11, 19, 14, 0, 0, DateTimeKind.Utc)),
            Now,
            other,
            link
        );

        Assert.True(moved.IsSuccess);
        Assert.Equal(other, appointment.OrganizerUserId);
        Assert.Equal(link, appointment.Location);
    }

    [Fact]
    public void The_office_cannot_move_or_cancel_a_public_demo_the_usual_way()
    {
        var appointment = Demo(Page);

        Assert.Equal(
            "Calendar.Appointment.ManagedByPublicPage",
            appointment.Cancel(Host, canManageAll: true, null, Now).Error.Code
        );
        Assert.Equal(
            "Calendar.Appointment.ManagedByPublicPage",
            appointment
                .Reschedule(At(new DateTime(2026, 11, 19, 14, 0, 0, DateTimeKind.Utc)), Host, canManageAll: true, Now)
                .Error.Code
        );
    }

    [Fact]
    public void The_team_moves_and_cancels_a_public_demo_through_its_booking_leaving_who_did_it()
    {
        var appointment = Demo(Page);
        var staff = Guid.NewGuid();

        Assert.True(
            appointment
                .RescheduleByPublicPageStaff(At(new DateTime(2026, 11, 19, 14, 0, 0, DateTimeKind.Utc)), staff, Now)
                .IsSuccess
        );
        var moved = appointment.DomainEvents.OfType<AppointmentRescheduledDomainEvent>().Last();
        Assert.Equal(staff, moved.MovedByUserId);

        Assert.True(appointment.CancelByPublicPageStaff(staff, "Host is sick", Now).IsSuccess);
        var cancelled = appointment.DomainEvents.OfType<AppointmentCancelledDomainEvent>().Single();
        Assert.Equal(staff, cancelled.CancelledByUserId);
        Assert.Equal("Host is sick", appointment.CancellationReason);
    }

    [Fact]
    public void Nothing_else_can_split_the_demo_from_its_booking()
    {
        var appointment = Demo(Page);
        var visitor = appointment.Attendees.Single(a => a.Kind == AttendeeKind.External);
        var other = Location.Create("https://zoom.us/j/1").Value;

        Assert.Equal(
            "Calendar.Appointment.ManagedByPublicPage",
            appointment.ChangeLocation(other, Host, true).Error.Code
        );
        Assert.Equal(
            "Calendar.Appointment.ManagedByPublicPage",
            appointment.RemoveAttendee(visitor.Id, Host, true).Error.Code
        );
        Assert.Equal(
            "Calendar.Appointment.ManagedByPublicPage",
            appointment.ReassignOrganizer(Guid.NewGuid()).Error.Code
        );
        Assert.Equal(
            "Calendar.Appointment.ManagedByPublicPage",
            appointment.EditDetails(appointment.Title, "Notes", other, Host, true, Now).Error.Code
        );
        // Las notas del anfitrión sí se pueden editar: el enlace no cambia.
        Assert.True(
            appointment.EditDetails(appointment.Title, "Notes", appointment.Location, Host, true, Now).IsSuccess
        );
    }

    [Fact]
    public void Forgetting_the_visitor_keeps_the_meeting_but_drops_who_it_was()
    {
        var appointment = Demo(Page);

        var forgotten = appointment.ForgetPublicVisitor(AppointmentTitle.Create("Product demo").Value);

        Assert.True(forgotten.IsSuccess);
        Assert.Equal("Product demo", appointment.Title.Value);
        var visitor = appointment.Attendees.Single(a => a.Kind == AttendeeKind.External);
        Assert.Equal("Anonymized", visitor.Snapshot.DisplayName);
        Assert.Null(visitor.Snapshot.Email);
        Assert.Equal(
            "Calendar.Appointment.NotFromPublicPage",
            Demo(bookingPageId: null).ForgetPublicVisitor(AppointmentTitle.Create("x").Value).Error.Code
        );
    }

    [Fact]
    public void Only_appointments_from_a_public_page_accept_external_changes()
    {
        var appointment = Demo(bookingPageId: null);

        Assert.Equal(
            "Calendar.Appointment.NotFromPublicPage",
            appointment.CancelByExternalRequester(null, Now).Error.Code
        );
    }
}
