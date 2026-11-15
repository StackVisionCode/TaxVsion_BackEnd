using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.PublicBookings.Events;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

public sealed class PublicBookingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Page = Guid.NewGuid();
    private static readonly Guid Host = Guid.NewGuid();
    private static readonly Guid OtherHost = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Slot = new(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc);

    internal static PublicBookingRequestData Data(string email = "jane@acme-tax.com") =>
        new(
            ProspectName.Create("Jane Doe").Value,
            WorkEmail.Create(email).Value,
            Application.TestRequesterKeyHasher.KeyFor(email),
            CompanyName.Create("Acme Tax").Value,
            OfficeSize.TwoToFive,
            ProspectPhone.Create("+1 305 555 0100").Value,
            DemoInterest.Create("E-signatures").Value,
            HeardAboutAnswer.Create("whatsapp", null, allowsDetail: false).Value,
            BookingAttribution.Create(BookingEntrySource.Hero, "google", "cpc", "fall", "google.com").Value,
            BookingLanguage.En,
            CalendarTimeZone.Create("America/New_York").Value,
            MarketingOptIn: false,
            ConsentTextVersion: "2026-11-demo-v1"
        );

    private static PublicBookingIssued Request(bool requiresVerification = true, string key = "key-1") =>
        PublicBooking
            .Request(Tenant, Page, Data(), Slot, 30, Host, key, requiresVerification, holdMinutes: 10, Now)
            .Value;

    private static (PublicBooking Booking, string Token) Confirmed()
    {
        var issued = Request();
        Assert.True(issued.Booking.VerifyCode(issued.Code!, Now.AddMinutes(1)).IsSuccess);
        var token = issued.Booking.Confirm(Host, Guid.NewGuid(), Now.AddMinutes(1));
        Assert.True(token.IsSuccess, token.IsFailure ? token.Error.Code : null);
        return (issued.Booking, token.Value);
    }

    [Fact]
    public void A_request_holds_the_slot_and_issues_a_code_and_a_handle()
    {
        var issued = Request();

        Assert.Equal(PublicBookingStatus.PendingVerification, issued.Booking.Status);
        Assert.Equal(Now.AddMinutes(10), issued.Booking.HoldExpiresAtUtc);
        Assert.Equal(Host, issued.Booking.HostUserId);
        Assert.Matches("^[0-9]{6}$", issued.Code!);
        Assert.False(string.IsNullOrWhiteSpace(issued.RequestHandle));
        Assert.Equal(OpaqueToken.HashOf(issued.RequestHandle), issued.Booking.RequestHandleHash);
        Assert.True(VerificationCode.Matches(issued.Booking.Id, issued.Code, issued.Booking.CodeHash));
    }

    [Fact]
    public void Secrets_never_travel_in_a_domain_event()
    {
        // Los eventos de dominio se guardan en la cola durable: un secreto ahí quedaría en claro en la base.
        var (booking, _) = Confirmed();
        booking.ResendCode(Now);
        booking.RescheduleByVisitor(Slot.AddDays(1), Host, Now.AddHours(1));
        booking.MarkReminderSent(24, Slot.AddHours(-2));
        booking.CancelByVisitor(Now.AddHours(3));

        var cancelled = Assert.IsType<PublicBookingCancelledDomainEvent>(Assert.Single(booking.DomainEvents));
        Assert.Equal(booking.Id, cancelled.BookingId);
    }

    [Fact]
    public void A_request_needs_a_keyed_requester_hash()
    {
        var data = Data() with { RequesterKey = "not-a-hmac" };

        Assert.Equal(
            "Calendar.PublicBooking.RequestInvalid",
            PublicBooking.Request(Tenant, Page, data, Slot, 30, Host, "k", true, 10, Now).Error.Code
        );
    }

    [Fact]
    public void A_slot_in_the_past_or_a_bad_key_is_rejected()
    {
        Assert.Equal(
            "Calendar.PublicBooking.SlotInPast",
            PublicBooking.Request(Tenant, Page, Data(), Now.AddMinutes(-1), 30, Host, "k", true, 10, Now).Error.Code
        );
        Assert.Equal(
            "Calendar.PublicBooking.IdempotencyKeyInvalid",
            PublicBooking.Request(Tenant, Page, Data(), Slot, 30, Host, new string('k', 101), true, 10, Now).Error.Code
        );
    }

    [Fact]
    public void A_wrong_code_counts_and_five_failures_expire_the_request()
    {
        var issued = Request();
        for (var attempt = 1; attempt < PublicBooking.MaxCodeAttempts; attempt++)
            Assert.Equal("Calendar.PublicBooking.InvalidCode", issued.Booking.VerifyCode("000000", Now).Error.Code);

        var last = issued.Booking.VerifyCode("000000", Now);

        Assert.Equal("Calendar.PublicBooking.TooManyAttempts", last.Error.Code);
        Assert.Equal(PublicBookingStatus.Expired, issued.Booking.Status);
    }

    [Fact]
    public void An_expired_hold_cannot_be_verified()
    {
        var issued = Request();

        var result = issued.Booking.VerifyCode(issued.Code!, Now.AddMinutes(11));

        Assert.Equal("Calendar.PublicBooking.HoldExpired", result.Error.Code);
    }

    [Fact]
    public void Confirming_needs_the_verified_code_and_returns_a_management_token()
    {
        var issued = Request();
        Assert.Equal(
            "Calendar.PublicBooking.NotVerified",
            issued.Booking.Confirm(Host, Guid.NewGuid(), Now).Error.Code
        );

        issued.Booking.VerifyCode(issued.Code!, Now);
        var appointment = Guid.NewGuid();
        var token = issued.Booking.Confirm(OtherHost, appointment, Now);

        Assert.True(token.IsSuccess);
        Assert.Equal(PublicBookingStatus.Confirmed, issued.Booking.Status);
        Assert.Equal(OtherHost, issued.Booking.HostUserId);
        Assert.Equal(appointment, issued.Booking.AppointmentId);
        Assert.Equal(OpaqueToken.HashOf(token.Value), issued.Booking.ManagementTokenHash);
        Assert.Null(issued.Booking.CodeHash);
        Assert.Equal(1, issued.Booking.ManagementTokenVersion);
    }

    [Fact]
    public void Without_verification_the_request_confirms_without_a_code()
    {
        var issued = Request(requiresVerification: false);

        Assert.Null(issued.Code);
        Assert.True(issued.Booking.Confirm(Host, Guid.NewGuid(), Now).IsSuccess);
    }

    [Fact]
    public void A_code_can_be_resent_up_to_the_limit()
    {
        var issued = Request();
        for (var send = 2; send <= PublicBooking.MaxCodeSends; send++)
        {
            var code = issued.Booking.ResendCode(Now);
            Assert.True(VerificationCode.Matches(issued.Booking.Id, code.Value, issued.Booking.CodeHash));
        }

        Assert.Equal("Calendar.PublicBooking.TooManyCodes", issued.Booking.ResendCode(Now).Error.Code);
    }

    [Fact]
    public void Another_slot_can_be_chosen_while_the_hold_lives_once_the_email_is_proven()
    {
        var issued = Request();
        Assert.Equal(
            "Calendar.PublicBooking.NotVerified",
            issued.Booking.ChooseSlot(Slot.AddHours(1), OtherHost, Now).Error.Code
        );
        issued.Booking.VerifyCode(issued.Code!, Now);

        Assert.True(issued.Booking.ChooseSlot(Slot.AddHours(1), OtherHost, Now).IsSuccess);
        Assert.Equal(Slot.AddHours(1), issued.Booking.SlotStartUtc);
        Assert.Equal(OtherHost, issued.Booking.HostUserId);
        Assert.Equal(
            "Calendar.PublicBooking.HoldExpired",
            issued.Booking.ChooseSlot(Slot, Host, Now.AddMinutes(11)).Error.Code
        );
    }

    [Fact]
    public void Rescheduling_rotates_the_token_and_bumps_the_calendar_sequence()
    {
        var (booking, token) = Confirmed();

        var moved = booking.RescheduleByVisitor(Slot.AddDays(1), Host, Now.AddHours(1));

        Assert.True(moved.IsSuccess);
        Assert.NotEqual(token, moved.Value);
        Assert.Equal(OpaqueToken.HashOf(moved.Value), booking.ManagementTokenHash);
        Assert.Equal(1, booking.IcsSequence);
        Assert.Equal(Slot.AddDays(1), booking.SlotStartUtc);
        Assert.Equal(2, booking.ManagementTokenVersion);
    }

    [Fact]
    public void Cancelling_closes_the_booking_and_voids_the_token()
    {
        var (booking, _) = Confirmed();

        Assert.True(booking.CancelByStaff(Admin, "Host unavailable", Now.AddHours(1)).IsSuccess);

        Assert.Equal(PublicBookingStatus.Cancelled, booking.Status);
        Assert.Null(booking.ManagementTokenHash);
        Assert.Equal(PublicBookingActor.Staff, booking.CancelledBy);
        Assert.Equal("Calendar.PublicBooking.NotConfirmed", booking.CancelByVisitor(Now.AddHours(2)).Error.Code);
    }

    [Fact]
    public void A_past_demo_cannot_be_changed_but_its_outcome_can_be_recorded()
    {
        var (booking, _) = Confirmed();
        var afterDemo = Slot.AddMinutes(31);

        Assert.Equal("Calendar.PublicBooking.AlreadyHappened", booking.CancelByVisitor(afterDemo).Error.Code);
        Assert.Equal(
            "Calendar.PublicBooking.NotFinishedYet",
            booking.MarkOutcome(PublicBookingOutcome.Completed, Admin, Slot).Error.Code
        );
        Assert.True(booking.MarkOutcome(PublicBookingOutcome.NoShow, Admin, afterDemo).IsSuccess);
        Assert.Equal(PublicBookingStatus.NoShow, booking.Status);
    }

    [Fact]
    public void Expiring_only_applies_to_pending_requests()
    {
        var issued = Request();
        Assert.True(issued.Booking.Expire(Now.AddMinutes(11)).IsSuccess);
        Assert.Equal(PublicBookingStatus.Expired, issued.Booking.Status);

        var (confirmed, _) = Confirmed();
        Assert.Equal("Calendar.PublicBooking.NotPending", confirmed.Expire(Now).Error.Code);
    }

    [Fact]
    public void A_retried_request_gets_a_new_handle_without_a_new_code()
    {
        var issued = Request();
        issued.Booking.ClearDomainEvents();

        var handle = issued.Booking.ReissueRequestHandle(Now.AddMinutes(1));

        Assert.True(handle.IsSuccess);
        Assert.NotEqual(issued.RequestHandle, handle.Value);
        Assert.Equal(OpaqueToken.HashOf(handle.Value), issued.Booking.RequestHandleHash);
        Assert.Empty(issued.Booking.DomainEvents);
        Assert.Equal(
            "Calendar.PublicBooking.HoldExpired",
            issued.Booking.ReissueRequestHandle(Now.AddMinutes(11)).Error.Code
        );
    }

    [Fact]
    public void A_new_request_from_the_same_visitor_replaces_the_pending_one()
    {
        var issued = Request();

        Assert.True(issued.Booking.ExpireReplaced(Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(PublicBookingStatus.Expired, issued.Booking.Status);

        var (confirmed, _) = Confirmed();
        Assert.Equal("Calendar.PublicBooking.NotPending", confirmed.ExpireReplaced(Now).Error.Code);
    }

    [Fact]
    public void The_reminder_is_marked_once()
    {
        var (booking, _) = Confirmed();

        var token = booking.MarkReminderSent(24, Now.AddDays(5));

        Assert.True(token.IsSuccess);
        Assert.Equal(OpaqueToken.HashOf(token.Value), booking.ManagementTokenHash);
        Assert.Equal(
            "Calendar.PublicBooking.ReminderAlreadySent",
            booking.MarkReminderSent(24, Now.AddDays(5)).Error.Code
        );
    }

    /// <summary>"Ya tienes una demo": un enlace nuevo por hora como mucho, y solo con la demo confirmada y por delante.</summary>
    [Fact]
    public void A_repeat_request_rotates_the_link_at_most_once_per_hour()
    {
        var (booking, old) = Confirmed();
        var version = booking.ManagementTokenVersion;

        var first = booking.NoteRepeatRequest(Now.AddHours(1));

        Assert.True(first.IsSuccess);
        Assert.Equal(version + 1, booking.ManagementTokenVersion);
        Assert.Equal(OpaqueToken.HashOf(first.Value), booking.ManagementTokenHash);
        Assert.NotEqual(OpaqueToken.HashOf(old), booking.ManagementTokenHash);
        Assert.Equal(
            "Calendar.PublicBooking.RepeatNoticeTooSoon",
            booking.NoteRepeatRequest(Now.AddHours(1).AddMinutes(59)).Error.Code
        );
        Assert.True(booking.NoteRepeatRequest(Now.AddHours(2)).IsSuccess);
        Assert.Equal("Calendar.PublicBooking.AlreadyHappened", booking.NoteRepeatRequest(Slot).Error.Code);
    }

    [Fact]
    public void A_pending_request_gets_no_repeat_notice()
    {
        var issued = Request();

        Assert.Equal("Calendar.PublicBooking.NotConfirmed", issued.Booking.NoteRepeatRequest(Now).Error.Code);
    }

    [Fact]
    public void The_reminder_waits_for_its_window()
    {
        var (booking, _) = Confirmed();

        Assert.Equal(
            "Calendar.PublicBooking.ReminderNotDue",
            booking.MarkReminderSent(24, Slot.AddHours(-25)).Error.Code
        );
        Assert.True(booking.MarkReminderSent(24, Slot.AddHours(-23)).IsSuccess);
    }

    [Fact]
    public void A_demo_booked_inside_the_window_gets_no_reminder()
    {
        // Confirmada a 2 horas de la demo: el correo de confirmación ya trae todo; un recordatorio sería ruido.
        var issued = PublicBooking
            .Request(Tenant, Page, Data(), Slot, 30, Host, "k", false, 10, Slot.AddHours(-2))
            .Value;
        issued.Booking.Confirm(Host, Guid.NewGuid(), Slot.AddHours(-2));

        Assert.Equal(
            "Calendar.PublicBooking.ReminderNotNeeded",
            issued.Booking.MarkReminderSent(24, Slot.AddHours(-1)).Error.Code
        );
    }

    [Fact]
    public void Anonymizing_wipes_the_personal_data()
    {
        var (booking, _) = Confirmed();
        booking.CancelByVisitor(Now.AddHours(1));

        Assert.True(booking.Anonymize(Now.AddYears(2)).IsSuccess);

        Assert.Equal("Anonymized", booking.Name);
        Assert.Null(booking.Email);
        Assert.Null(booking.Phone);
        Assert.Null(booking.Interest);
        Assert.NotNull(booking.AnonymizedAtUtc);
    }
}
