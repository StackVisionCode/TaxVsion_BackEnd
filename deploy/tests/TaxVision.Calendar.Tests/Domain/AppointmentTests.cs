using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

public sealed class AppointmentTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Organizer = Guid.NewGuid();
    private static readonly Guid Attendee = Guid.NewGuid();
    private static readonly Guid TypeId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static EventTiming Timing(int hour = 14) =>
        EventTiming
            .PointInTimeOf(
                new DateTime(2026, 3, 10, hour, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 10, hour + 1, 0, 0, DateTimeKind.Utc),
                "America/New_York"
            )
            .Value;

    private static Appointment Scheduled()
    {
        var appointment = Appointment.Schedule(
            Tenant,
            AppointmentTitle.Create("Revision de la declaracion").Value,
            Timing(),
            TypeId,
            Organizer,
            Now
        );

        Assert.True(appointment.IsSuccess);
        return appointment.Value;
    }

    private static Appointment ScheduledWithAttendee()
    {
        var appointment = Scheduled();
        var added = appointment.AddAttendee(
            AttendeeKind.InternalUser,
            Attendee,
            null,
            AttendeeSnapshot.Create("Ana Preparadora", "ana@firma.test").Value,
            isRequired: true,
            Organizer,
            Now
        );

        Assert.True(added.IsSuccess);
        return appointment;
    }

    [Fact]
    public void Scheduling_records_the_organizer_and_confirms_the_appointment()
    {
        var appointment = Scheduled();

        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(Organizer, appointment.OrganizerUserId);
        Assert.Equal(Tenant, appointment.TenantId);
        Assert.Single(appointment.DomainEvents);
    }

    [Fact]
    public void An_attendee_who_is_not_the_organizer_cannot_reschedule()
    {
        var appointment = ScheduledWithAttendee();

        var result = appointment.Reschedule(Timing(16), Attendee, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Appointment.NotTheOrganizer", result.Error.Code);
    }

    [Fact]
    public void An_attendee_who_is_not_the_organizer_can_respond()
    {
        var appointment = ScheduledWithAttendee();

        var result = appointment.RespondAsAttendee(AttendeeResponse.Accepted, Attendee, null, null, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendeeResponse.Accepted, appointment.Attendees[0].Response);
        Assert.Equal(Now, appointment.Attendees[0].RespondedAtUtc);
    }

    [Fact]
    public void Rescheduling_clears_the_answers_that_were_given_for_the_old_time()
    {
        var appointment = ScheduledWithAttendee();
        appointment.RespondAsAttendee(AttendeeResponse.Accepted, Attendee, null, null, Now);

        var result = appointment.Reschedule(Timing(20), Organizer, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendeeResponse.NeedsAction, appointment.Attendees[0].Response);
        Assert.Null(appointment.Attendees[0].RespondedAtUtc);
    }

    [Fact]
    public void Only_the_organizer_cancels()
    {
        var appointment = ScheduledWithAttendee();

        var byAttendee = appointment.Cancel(Attendee, "no puedo", Now);
        var byOrganizer = appointment.Cancel(Organizer, "el cliente reprogramo", Now);

        Assert.True(byAttendee.IsFailure);
        Assert.True(byOrganizer.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("el cliente reprogramo", appointment.CancellationReason);
    }

    [Fact]
    public void A_cancelled_appointment_cannot_be_moved()
    {
        var appointment = Scheduled();
        appointment.Cancel(Organizer, null, Now);

        var result = appointment.Reschedule(Timing(18), Organizer, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Appointment.CancelledIsFinal", result.Error.Code);
    }

    [Fact]
    public void The_same_person_cannot_be_invited_twice()
    {
        var appointment = ScheduledWithAttendee();

        var again = appointment.AddAttendee(
            AttendeeKind.InternalUser,
            Attendee,
            null,
            AttendeeSnapshot.Create("Ana Preparadora", "ana@firma.test").Value,
            isRequired: false,
            Organizer,
            Now
        );

        Assert.True(again.IsFailure);
        Assert.Equal("Calendar.Appointment.AttendeeAlreadyAdded", again.Error.Code);
        Assert.Single(appointment.Attendees);
    }

    [Fact]
    public void Someone_who_was_never_invited_cannot_respond()
    {
        var appointment = ScheduledWithAttendee();

        var result = appointment.RespondAsAttendee(AttendeeResponse.Accepted, Guid.NewGuid(), null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Calendar.Appointment.AttendeeNotFound", result.Error.Code);
    }

    [Fact]
    public void An_external_attendee_is_matched_by_email()
    {
        var appointment = Scheduled();
        appointment.AddAttendee(
            AttendeeKind.External,
            null,
            null,
            AttendeeSnapshot.Create("Contador externo", "externo@otra.test").Value,
            isRequired: false,
            Organizer,
            Now
        );

        var result = appointment.RespondAsAttendee(AttendeeResponse.Tentative, null, null, "EXTERNO@OTRA.TEST", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendeeResponse.Tentative, appointment.Attendees[0].Response);
    }
}
