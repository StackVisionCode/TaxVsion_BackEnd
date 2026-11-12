using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
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
            Now,
            canManageAll: false
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

        var result = appointment.Reschedule(Timing(16), Attendee, canManageAll: false, Now);

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
    public void A_response_does_not_change_the_schedule_stamp_but_a_new_time_does()
    {
        var appointment = ScheduledWithAttendee();
        var stamp = appointment.ScheduleStamp;

        appointment.RespondAsAttendee(AttendeeResponse.Accepted, Attendee, null, null, Now);
        Assert.Equal(stamp, appointment.ScheduleStamp);

        appointment.Reschedule(Timing(20), Organizer, canManageAll: false, Now);
        Assert.NotEqual(stamp, appointment.ScheduleStamp);
    }

    [Fact]
    public void Rescheduling_clears_the_answers_that_were_given_for_the_old_time()
    {
        var appointment = ScheduledWithAttendee();
        appointment.RespondAsAttendee(AttendeeResponse.Accepted, Attendee, null, null, Now);

        var result = appointment.Reschedule(Timing(20), Organizer, canManageAll: false, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendeeResponse.NeedsAction, appointment.Attendees[0].Response);
        Assert.Null(appointment.Attendees[0].RespondedAtUtc);
    }

    [Fact]
    public void Only_the_organizer_cancels()
    {
        var appointment = ScheduledWithAttendee();

        var byAttendee = appointment.Cancel(Attendee, canManageAll: false, "no puedo", Now);
        var byOrganizer = appointment.Cancel(Organizer, canManageAll: false, "el cliente reprogramo", Now);

        Assert.True(byAttendee.IsFailure);
        Assert.True(byOrganizer.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("el cliente reprogramo", appointment.CancellationReason);
    }

    [Fact]
    public void A_cancelled_appointment_cannot_be_moved()
    {
        var appointment = Scheduled();
        appointment.Cancel(Organizer, canManageAll: false, null, Now);

        var result = appointment.Reschedule(Timing(18), Organizer, canManageAll: false, Now);

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
            Now,
            canManageAll: false
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
            Now,
            canManageAll: false
        );

        var result = appointment.RespondAsAttendee(AttendeeResponse.Tentative, null, null, "EXTERNO@OTRA.TEST", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendeeResponse.Tentative, appointment.Attendees[0].Response);
    }

    /// <summary>
    /// A7 — `calendar.manage_all` existía en el catalogo sin gatear nada. Sin él, una cita
    /// quedaba congelada cuando su organizador se iba o se enfermaba: ni el dueño de la oficina podía
    /// cancelarla. El override AGREGA capacidad; el organizador sigue pudiendo con sus propias citas.
    /// </summary>
    [Fact]
    public void The_office_override_cancels_an_appointment_of_someone_else()
    {
        var appointment = ScheduledWithAttendee();

        var byAnotherUser = appointment.Cancel(Guid.NewGuid(), canManageAll: true, "el organizador no está", Now);

        Assert.True(byAnotherUser.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
    }

    [Fact]
    public void Without_the_override_a_stranger_still_cannot_reschedule()
    {
        var appointment = Scheduled();

        var moved = appointment.Reschedule(Timing(18), Guid.NewGuid(), canManageAll: false, Now);

        Assert.True(moved.IsFailure);
    }

    [Fact]
    public void Removing_a_departed_staff_attendee_drops_only_that_user()
    {
        var appointment = ScheduledWithAttendee();

        var removed = appointment.RemoveDepartedStaffAttendee(Attendee);

        Assert.True(removed.IsSuccess);
        Assert.DoesNotContain(appointment.Attendees, a => a.UserId == Attendee);
    }

    [Fact]
    public void Removing_a_departed_staff_attendee_again_is_a_no_op_failure()
    {
        var appointment = ScheduledWithAttendee();
        appointment.RemoveDepartedStaffAttendee(Attendee);

        var again = appointment.RemoveDepartedStaffAttendee(Attendee);

        Assert.Equal(AppointmentErrors.AttendeeNotFound, again.Error);
    }

    [Fact]
    public void A_cancelled_appointment_keeps_its_attendees_as_history()
    {
        var appointment = ScheduledWithAttendee();
        appointment.Cancel(Organizer, canManageAll: false, reason: null, Now);

        var removed = appointment.RemoveDepartedStaffAttendee(Attendee);

        Assert.Equal(AppointmentErrors.CancelledIsFinal, removed.Error);
        Assert.Contains(appointment.Attendees, a => a.UserId == Attendee);
    }

    [Fact]
    public void The_organizer_is_not_removed_as_a_departed_attendee()
    {
        var appointment = Scheduled();

        var removed = appointment.RemoveDepartedStaffAttendee(Organizer);

        Assert.Equal(AppointmentErrors.OrganizerCannotBeRemoved, removed.Error);
    }

    private static string InviteManuel(Appointment appointment)
    {
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
        return ((AppointmentAttendeeInvitedDomainEvent)appointment.DomainEvents[^1]).RsvpToken!;
    }

    [Fact]
    public void Each_attendee_answers_by_their_own_rsvp_link_and_only_its_hash_is_kept()
    {
        var appointment = ScheduledWithAttendee();
        var token = InviteManuel(appointment);
        var ana = appointment.Attendees[0];
        var manuel = appointment.Attendees[1];

        Assert.Equal(RsvpToken.HashOf(token), manuel.RsvpTokenHash);
        Assert.NotEqual(ana.RsvpTokenHash, manuel.RsvpTokenHash);

        var result = appointment.RespondByRsvp(token, AttendeeResponse.Accepted, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendeeResponse.Accepted, manuel.Response);
        Assert.Equal(AttendeeResponse.NeedsAction, ana.Response);
        Assert.IsType<AppointmentAttendeeRespondedDomainEvent>(appointment.DomainEvents[^1]);
    }

    [Fact]
    public void Moving_the_appointment_replaces_the_links_so_the_old_email_no_longer_answers()
    {
        var appointment = ScheduledWithAttendee();
        var old = InviteManuel(appointment);

        appointment.Reschedule(Timing(16), Organizer, canManageAll: false, Now);
        var moved = (AppointmentRescheduledDomainEvent)appointment.DomainEvents[^1];
        var fresh = Array.Find(moved.RsvpTokens!, t => t.AttendeeId == appointment.Attendees[1].Id)!.Token;

        Assert.Equal(2, moved.RsvpTokens!.Length);
        Assert.Equal(
            AppointmentErrors.AttendeeNotFound,
            appointment.RespondByRsvp(old, AttendeeResponse.Accepted, Now).Error
        );
        Assert.True(appointment.RespondByRsvp(fresh, AttendeeResponse.Accepted, Now).IsSuccess);
    }

    [Fact]
    public void An_unknown_link_a_cancelled_or_past_appointment_and_no_answer_do_not_answer()
    {
        var appointment = ScheduledWithAttendee();
        var token = InviteManuel(appointment);
        var afterEnd = new DateTime(2026, 3, 10, 15, 1, 0, DateTimeKind.Utc);

        Assert.Equal(
            AppointmentErrors.AttendeeNotFound,
            appointment.RespondByRsvp("not-a-token", AttendeeResponse.Accepted, Now).Error
        );
        Assert.Equal(
            AppointmentErrors.InvalidResponse,
            appointment.RespondByRsvp(token, AttendeeResponse.NeedsAction, Now).Error
        );
        Assert.Equal(
            AppointmentErrors.RsvpClosed,
            appointment.RespondByRsvp(token, AttendeeResponse.Accepted, afterEnd).Error
        );

        appointment.Cancel(Organizer, canManageAll: false, null, Now);
        Assert.Equal(
            AppointmentErrors.CancelledIsFinal,
            appointment.RespondByRsvp(token, AttendeeResponse.Accepted, Now).Error
        );
    }

    [Fact]
    public void The_organizer_edits_the_details_and_the_change_is_announced_with_what_changed()
    {
        var appointment = ScheduledWithAttendee();
        appointment.ClearDomainEvents();

        var result = appointment.EditDetails(
            AppointmentTitle.Create("Revision final").Value,
            "Bring your W-2",
            Location.Create("Office 2").Value,
            Organizer,
            canManageAll: false,
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Revision final", appointment.Title.Value);
        Assert.Equal("Office 2", appointment.Location?.Value);
        var changed = Assert.IsType<AppointmentDetailsChangedDomainEvent>(Assert.Single(appointment.DomainEvents));
        Assert.Equal("Revision de la declaracion", changed.PreviousTitle);
        Assert.Equal(["Title", "Description", "Location"], changed.ChangedFields);
        // La hora y las respuestas no se tocan.
        Assert.Equal(Timing(), appointment.Timing);
    }

    [Fact]
    public void Saving_the_same_details_announces_nothing_and_only_the_organizer_or_manage_all_edits()
    {
        var appointment = ScheduledWithAttendee();
        appointment.ClearDomainEvents();

        var same = appointment.EditDetails(appointment.Title, null, null, Organizer, canManageAll: false, Now);
        var byAttendee = appointment.EditDetails(
            AppointmentTitle.Create("Otra").Value,
            null,
            null,
            Attendee,
            canManageAll: false,
            Now
        );
        var byManager = appointment.EditDetails(
            AppointmentTitle.Create("Otra").Value,
            null,
            null,
            Attendee,
            canManageAll: true,
            Now
        );

        Assert.True(same.IsSuccess);
        Assert.Equal("Calendar.Appointment.NotTheOrganizer", byAttendee.Error.Code);
        Assert.True(byManager.IsSuccess);
        Assert.Single(appointment.DomainEvents);
    }

    [Fact]
    public void A_cancelled_appointment_or_a_too_long_description_is_not_edited()
    {
        var appointment = ScheduledWithAttendee();
        var tooLong = appointment.EditDetails(
            appointment.Title,
            new string('x', Appointment.MaxDescriptionLength + 1),
            null,
            Organizer,
            false,
            Now
        );
        appointment.Cancel(Organizer, canManageAll: false, null, Now);
        var cancelled = appointment.EditDetails(
            AppointmentTitle.Create("Otra").Value,
            null,
            null,
            Organizer,
            false,
            Now
        );

        Assert.Equal(AppointmentErrors.DescriptionTooLong, tooLong.Error);
        Assert.Equal(AppointmentErrors.CancelledIsFinal, cancelled.Error);
    }

    private static readonly Guid Client = Guid.NewGuid();

    private static Appointment ScheduledForClient()
    {
        var appointment = Appointment
            .Schedule(
                Tenant,
                AppointmentTitle.Create("Revision de la declaracion").Value,
                Timing(),
                TypeId,
                Organizer,
                Now,
                customerId: Client,
                attendees:
                [
                    new InitialAttendee(
                        AttendeeKind.Customer,
                        null,
                        Client,
                        AttendeeSnapshot.Create("Ana Client", "ana@client.test").Value,
                        true,
                        Accepted: true
                    ),
                    new InitialAttendee(
                        AttendeeKind.InternalUser,
                        Attendee,
                        null,
                        AttendeeSnapshot.Create("Andres", "andres@firma.test").Value,
                        true
                    ),
                ]
            )
            .Value;
        appointment.RespondAsAttendee(AttendeeResponse.Accepted, Attendee, null, null, Now);
        appointment.ClearDomainEvents();
        return appointment;
    }

    [Fact]
    public void The_client_moves_their_appointment_stays_going_and_everyone_else_is_asked_again()
    {
        var appointment = ScheduledForClient();

        var result = appointment.RescheduleByCustomer(Timing(16), Client, Guid.NewGuid(), Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttendeeResponse.Accepted, appointment.Attendees.Single(a => a.CustomerId == Client).Response);
        Assert.Equal(AttendeeResponse.NeedsAction, appointment.Attendees.Single(a => a.UserId == Attendee).Response);
        // Un solo hecho: se movió. Nada de un "respondió" aparte.
        Assert.IsType<AppointmentRescheduledDomainEvent>(Assert.Single(appointment.DomainEvents));
    }

    [Fact]
    public void Only_the_appointments_client_cancels_or_moves_it_and_never_a_series()
    {
        var appointment = ScheduledForClient();
        var stranger = Guid.NewGuid();

        Assert.Equal(
            AppointmentErrors.NotTheCustomer,
            appointment.CancelByCustomer(stranger, Guid.NewGuid(), null, Now).Error
        );
        Assert.Equal(
            AppointmentErrors.NotTheCustomer,
            appointment.RescheduleByCustomer(Timing(16), stranger, Guid.NewGuid(), Now).Error
        );

        var cancelled = appointment.CancelByCustomer(Client, Guid.NewGuid(), "Can't make it", Now);
        Assert.True(cancelled.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.IsType<AppointmentCancelledDomainEvent>(Assert.Single(appointment.DomainEvents));

        var series = ScheduledForClient();
        series.MakeRecurring(
            RecurrenceRule.Create("FREQ=WEEKLY").Value,
            EventTiming
                .RecurringOf(new DateOnly(2026, 3, 10), new TimeOnly(10, 0), TimeSpan.FromHours(1), "America/New_York")
                .Value,
            Organizer,
            canManageAll: false
        );
        Assert.Equal(
            AppointmentErrors.SeriesChangedByOffice,
            series.CancelByCustomer(Client, Guid.NewGuid(), null, Now).Error
        );
    }

    private static Appointment Requested()
    {
        var appointment = Appointment
            .Schedule(
                Tenant,
                AppointmentTitle.Create("Consulta inicial").Value,
                Timing(),
                TypeId,
                Organizer,
                Now,
                customerId: Client,
                attendees:
                [
                    new InitialAttendee(
                        AttendeeKind.Customer,
                        null,
                        Client,
                        AttendeeSnapshot.Create("Minato", "minato@client.test").Value,
                        true,
                        Accepted: true
                    ),
                ],
                bookedByCustomerName: "Minato",
                awaitingApproval: true
            )
            .Value;
        return appointment;
    }

    [Fact]
    public void A_booking_that_needs_approval_waits_and_announces_only_the_request()
    {
        var appointment = Requested();

        Assert.Equal(AppointmentStatus.Requested, appointment.Status);
        // Ni sala, ni avisos de cita, ni recordatorios: solo "hay una solicitud".
        var requested = Assert.IsType<AppointmentBookingRequestedDomainEvent>(Assert.Single(appointment.DomainEvents));
        Assert.Equal("Minato", requested.CustomerName);
    }

    [Fact]
    public void The_office_approves_the_request_and_only_then_it_is_scheduled()
    {
        var appointment = Requested();
        appointment.ClearDomainEvents();

        Assert.Equal(
            "Calendar.Appointment.NotTheOrganizer",
            appointment.ApproveRequest(Attendee, false, null, Now).Error.Code
        );
        var approved = appointment.ApproveRequest(Organizer, false, null, Now);

        Assert.True(approved.IsSuccess);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.IsType<AppointmentScheduledDomainEvent>(Assert.Single(appointment.DomainEvents));
        Assert.Equal(AppointmentErrors.NotARequest, appointment.ApproveRequest(Organizer, false, null, Now).Error);
    }

    [Fact]
    public void Approving_can_hand_the_appointment_to_another_employee()
    {
        var appointment = Requested();

        var approved = appointment.ApproveRequest(
            Organizer,
            false,
            new AppointmentAssignee(Attendee, AttendeeSnapshot.Create("Andres", "andres@firma.test").Value),
            Now
        );

        Assert.True(approved.IsSuccess);
        Assert.Equal(Attendee, appointment.OrganizerUserId);
        Assert.Equal(AttendeeResponse.Accepted, appointment.Attendees.Single(a => a.UserId == Attendee).Response);
        Assert.DoesNotContain(appointment.Attendees, a => a.UserId == Organizer);
    }

    [Fact]
    public void Declining_frees_the_time_and_tells_the_client_why()
    {
        var appointment = Requested();
        appointment.ClearDomainEvents();

        var declined = appointment.DeclineRequest(Organizer, false, "We're closed that day", Now);

        Assert.True(declined.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        var evt = Assert.IsType<AppointmentBookingDeclinedDomainEvent>(Assert.Single(appointment.DomainEvents));
        Assert.Equal("We're closed that day", evt.Reason);
        Assert.Equal(
            AppointmentErrors.NotARequest,
            ScheduledWithAttendee().DeclineRequest(Organizer, false, null, Now).Error
        );
    }
}
