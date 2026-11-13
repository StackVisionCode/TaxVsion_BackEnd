using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Tests.Domain;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// El flujo del visitante sin cuenta, de punta a punta con dobles en memoria: ver la página, ver horarios,
/// pedir, verificar, gestionar. La carrera real contra SQL está en las pruebas de integración.
/// </summary>
public sealed class PublicBookingFlowTests
{
    private static readonly Guid Tenant = PlatformTenant.Id;
    private static readonly Guid Admin = Guid.NewGuid();

    private readonly Guid _ana = new("00000000-0000-0000-0000-00000000000a");
    private readonly Guid _ben = new("00000000-0000-0000-0000-00000000000b");
    private readonly AppointmentType _type = BookingPageTests.DemoType(Tenant);
    private readonly InMemoryAppointments _appointments = new();
    private readonly NineToFiveAvailability _availability = new();
    private readonly PublicBookingStore _store = new();
    private readonly FakePublicBookingBot _bot = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();
    private readonly IOptions<PublicBookingOptions> _options = Options.Create(new PublicBookingOptions());
    private readonly JsonProtectedPayloadCodec _codec = new(
        new AesGcmRotatingSecretProtector(
            Options.Create(
                new RotatingSecretProtectionOptions
                {
                    MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    MasterKeyVersion = 1,
                }
            )
        )
    );
    private readonly BookingPage _page;

    public PublicBookingFlowTests()
    {
        _page = BookingPageTests.NewPage();
        _page.SetTenant(Tenant);
        _page.ChangeAppointmentType(_type, Admin, DateTime.UtcNow);
        _page.SetHosts(
            [
                BookingPageTests.Host(_ana, order: 0),
                BookingPageTests.Host(_ben, order: 1) with
                {
                    MeetingUrl = "https://zoom.us/j/1",
                },
            ],
            Admin,
            DateTime.UtcNow
        );
        _page.SetHeardAboutOptions(
            [
                BookingPageTests.Option("whatsapp"),
                BookingPageTests.Option("other", allowsDetail: true) with
                {
                    SortOrder = 1,
                },
            ],
            Admin,
            DateTime.UtcNow
        );
        _page.ChangeConsent(
            BookingPageTests.Privacy,
            BookingPageTests.Marketing,
            "2026-11-demo-v1",
            Admin,
            DateTime.UtcNow
        );
        _page.Activate(Admin, DateTime.UtcNow);
        _store.Pages.Add(_page);
        _availability.Add(_ana);
        _availability.Add(_ben);
    }

    // ---------- Página y horarios ----------

    [Fact]
    public async Task The_page_speaks_the_visitor_language_and_hands_out_the_site_key()
    {
        var english = await PageAsync("en");
        var spanish = await PageAsync("es");

        Assert.True(english.IsSuccess);
        Assert.Equal("Book a live demo", english.Value.Title);
        Assert.Equal("Agenda una demo en vivo", spanish.Value.Title);
        Assert.Equal(30, english.Value.DurationMinutes);
        Assert.True(english.Value.BookingAvailable);
        Assert.Equal("site-key", english.Value.TurnstileSiteKey);
        Assert.Equal(["whatsapp", "other"], english.Value.HeardAboutOptions.Select(o => o.Code));
    }

    /// <summary>
    /// El landing muestra quién da la demo antes de pedir datos (DEMO-11). Solo el nombre, en orden: ni el email de
    /// avisos ni el enlace de la reunión salen en la página pública.
    /// </summary>
    [Fact]
    public async Task The_page_names_who_gives_the_demo_and_nothing_else_about_them()
    {
        var page = (await PageAsync("en")).Value;

        Assert.Equal(["Ana Pérez", "Ana Pérez"], page.Hosts.Select(h => h.DisplayName));
        var json = System.Text.Json.JsonSerializer.Serialize(page);
        Assert.DoesNotContain("ana@taxoffix.io", json);
        Assert.DoesNotContain("meet.google.com", json);
        Assert.DoesNotContain("zoom.us", json);
    }

    [Fact]
    public async Task Without_bot_protection_the_page_says_booking_is_not_available()
    {
        _bot.Ready = false;

        var page = await PageAsync("en");

        Assert.False(page.Value.BookingAvailable);
        Assert.Null(page.Value.TurnstileSiteKey);
    }

    [Fact]
    public async Task A_page_of_a_tenant_that_is_not_allowed_does_not_exist()
    {
        _page.SetTenant(Guid.NewGuid());

        Assert.Equal("Calendar.PublicBooking.NotFound", (await PageAsync("en")).Error.Code);
    }

    [Fact]
    public async Task Slots_are_the_union_of_the_hosts_and_never_say_who()
    {
        var slots = await SlotsAsync();

        Assert.NotEmpty(slots);
        Assert.Equal(slots.Distinct().Count(), slots.Count);
        Assert.All(slots, start => Assert.True(start >= DateTime.UtcNow.AddMinutes(_page.MinNoticeMinutes)));
    }

    [Fact]
    public async Task A_slot_held_by_every_host_is_no_longer_offered()
    {
        var slot = (await SlotsAsync())[0];
        await RequestAsync(slot, "first@acme-tax.com", key: "first");
        Assert.Contains(slot, await SlotsAsync());

        await RequestAsync(slot, "second@acme-tax.com", key: "second");

        Assert.DoesNotContain(slot, await SlotsAsync());
    }

    // ---------- Pedir ----------

    [Fact]
    public async Task A_request_holds_the_slot_and_the_code_leaves_only_encrypted()
    {
        var slot = (await SlotsAsync())[0];

        var accepted = await RequestAsync(slot);

        Assert.True(accepted.IsSuccess);
        var booking = Assert.Single(_store.Bookings);
        Assert.Equal(PublicBookingStatus.PendingVerification, booking.Status);
        Assert.Equal(_ana, booking.HostUserId);
        Assert.Equal(OpaqueToken.HashOf(accepted.Value.RequestHandle), booking.RequestHandleHash);
        Assert.Empty(booking.DomainEvents);
        var sent = Assert.Single(_bus.Published.OfType<PublicBookingCodeRequestedIntegrationEvent>());
        Assert.True(VerificationCode.Matches(booking.Id, CodeOf(booking), booking.CodeHash));
        Assert.DoesNotContain(CodeOf(booking), sent.ProtectedCode.Cipher);
    }

    [Fact]
    public async Task A_bot_gets_the_same_answer_but_nothing_happens()
    {
        _bot.Passes = false;

        var accepted = await RequestAsync((await SlotsAsync())[0]);

        Assert.True(accepted.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(accepted.Value.RequestHandle));
        Assert.Empty(_store.Bookings);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Past_the_hourly_quota_the_same_answer_comes_back_without_effects()
    {
        var slots = await SlotsAsync();
        for (var i = 0; i < _options.Value.MaxRequestsPerEmailPerHour; i++)
            await RequestAsync(slots[i], key: $"key-{i}");
        var before = _store.Bookings.Count;

        var accepted = await RequestAsync(slots[5], key: "key-extra");

        Assert.True(accepted.IsSuccess);
        Assert.Equal(before, _store.Bookings.Count);
    }

    [Fact]
    public async Task A_retry_with_the_same_key_reuses_the_request_without_a_new_code()
    {
        var slot = (await SlotsAsync())[0];
        var first = await RequestAsync(slot, key: "same");
        _bus.Published.Clear();

        var retry = await RequestAsync(slot, key: "same");

        Assert.True(retry.IsSuccess);
        Assert.Single(_store.Bookings);
        Assert.NotEqual(first.Value.RequestHandle, retry.Value.RequestHandle);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_new_request_from_the_same_visitor_replaces_the_pending_one()
    {
        var slots = await SlotsAsync();
        await RequestAsync(slots[0], key: "first");

        await RequestAsync(slots[3], key: "second");

        Assert.Equal(PublicBookingStatus.Expired, _store.Bookings[0].Status);
        Assert.Equal(PublicBookingStatus.PendingVerification, _store.Bookings[1].Status);
    }

    [Fact]
    public async Task A_failed_replacement_keeps_the_previous_request()
    {
        var slots = await SlotsAsync();
        await RequestAsync(slots[0], key: "first");

        var failed = await RequestAsync(DateTime.UtcNow.AddMinutes(30), key: "second");

        Assert.Equal("Calendar.PublicBooking.SlotTaken", failed.Error.Code);
        Assert.Equal(PublicBookingStatus.PendingVerification, _store.Bookings[0].Status);
    }

    [Fact]
    public async Task An_abandoned_hold_does_not_block_the_slot_for_the_next_visitor()
    {
        var slot = (await SlotsAsync())[0];
        var abandoned = PublicBooking
            .Request(
                Tenant,
                _page.Id,
                PublicBookingTests.Data("gone@acme-tax.com"),
                slot,
                30,
                _ana,
                "old",
                true,
                10,
                DateTime.UtcNow.AddMinutes(-20)
            )
            .Value.Booking;
        _store.Add(abandoned);

        var accepted = await RequestAsync(slot);

        Assert.True(accepted.IsSuccess);
        Assert.Equal(PublicBookingStatus.Expired, abandoned.Status);
        Assert.Equal(_ana, _store.Bookings[1].HostUserId);
    }

    [Fact]
    public async Task A_required_field_left_blank_is_rejected()
    {
        _page.ConfigureFields(
            BookingFieldMode.Optional,
            BookingFieldMode.Required,
            BookingFieldMode.Optional,
            Admin,
            DateTime.UtcNow
        );

        var result = await RequestAsync((await SlotsAsync())[0], officeSize: null);

        Assert.Equal("Calendar.PublicBooking.FieldRequired", result.Error.Code);
    }

    [Fact]
    public async Task An_answer_that_is_not_on_the_page_is_rejected()
    {
        var result = await RequestAsync((await SlotsAsync())[0], heardAbout: "tiktok");

        Assert.Equal("Calendar.PublicBooking.HeardAboutRequired", result.Error.Code);
    }

    [Fact]
    public async Task A_taken_slot_answers_the_same_for_any_email()
    {
        var slots = await SlotsAsync();
        await ConfirmedTokenAsync(slots[0]);
        await RequestAsync(slots[2], "x@acme-tax.com", key: "x");
        await RequestAsync(slots[2], "y@acme-tax.com", key: "y");

        // Jane ya tiene una demo: aun así un horario ocupado le responde igual que a un email nuevo.
        var known = await RequestAsync(slots[2], key: "known");
        var fresh = await RequestAsync(slots[2], "new@acme-tax.com", key: "fresh");

        Assert.Equal("Calendar.PublicBooking.SlotTaken", known.Error.Code);
        Assert.Equal("Calendar.PublicBooking.SlotTaken", fresh.Error.Code);
    }

    // ---------- Verificar ----------

    [Fact]
    public async Task Verifying_creates_the_host_appointment_and_sends_the_token_encrypted()
    {
        var slot = (await SlotsAsync())[0];
        var accepted = await RequestAsync(slot);

        var confirmed = await VerifyAsync(accepted.Value.RequestHandle, CodeOf(_store.Bookings[0]));

        Assert.True(confirmed.IsSuccess, confirmed.IsFailure ? confirmed.Error.Code : null);
        Assert.Equal("Confirmed", confirmed.Value.Status);
        Assert.Equal(slot, confirmed.Value.StartUtc);
        Assert.Equal("https://meet.google.com/abc-defg-hij", confirmed.Value.MeetingUrl);
        var appointment = Assert.Single(_appointments.All);
        Assert.Equal(_ana, appointment.OrganizerUserId);
        Assert.Equal(_page.Id, appointment.BookingPageId);
        Assert.False(appointment.IsVirtual);
        Assert.Equal("https://meet.google.com/abc-defg-hij", appointment.Location!.Value);
        var visitor = Assert.Single(appointment.Attendees);
        Assert.Equal(AttendeeKind.External, visitor.Kind);
        Assert.Equal("jane@acme-tax.com", visitor.Snapshot.Email);
        Assert.Equal(appointment.Id, _store.Bookings[0].AppointmentId);
        Assert.Equal(OpaqueToken.HashOf(TokenOf(_store.Bookings[0])), _store.Bookings[0].ManagementTokenHash);
    }

    [Fact]
    public async Task If_the_held_host_got_busy_another_free_host_takes_the_demo()
    {
        var slot = (await SlotsAsync())[0];
        var accepted = await RequestAsync(slot);
        _availability.Block(_ana, slot);

        var confirmed = await VerifyAsync(accepted.Value.RequestHandle, CodeOf(_store.Bookings[0]));

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(_ben, _appointments.All[0].OrganizerUserId);
        Assert.Equal("https://zoom.us/j/1", confirmed.Value.MeetingUrl);
    }

    [Fact]
    public async Task With_no_free_host_left_the_request_stays_pending_and_the_slot_is_taken()
    {
        var slot = (await SlotsAsync())[0];
        var accepted = await RequestAsync(slot);
        _availability.Block(_ana, slot);
        _availability.Block(_ben, slot);

        var confirmed = await VerifyAsync(accepted.Value.RequestHandle, CodeOf(_store.Bookings[0]));

        Assert.Equal("Calendar.PublicBooking.SlotTaken", confirmed.Error.Code);
        Assert.Equal(PublicBookingStatus.PendingVerification, _store.Bookings[0].Status);
        Assert.Empty(_appointments.All);
    }

    [Fact]
    public async Task A_wrong_code_is_counted_and_an_unknown_handle_looks_the_same()
    {
        var accepted = await RequestAsync((await SlotsAsync())[0]);

        var wrong = await VerifyAsync(accepted.Value.RequestHandle, "000000");
        var unknown = await VerifyAsync("not-a-handle", "123456");

        Assert.Equal("Calendar.PublicBooking.InvalidCode", wrong.Error.Code);
        Assert.Equal("Calendar.PublicBooking.InvalidCode", unknown.Error.Code);
        Assert.Equal(1, _store.Bookings[0].CodeAttempts);
        Assert.True(_unitOfWork.Saves > 0);
    }

    [Fact]
    public async Task Another_slot_can_be_chosen_once_the_email_is_proven_without_a_new_code()
    {
        var slots = await SlotsAsync();
        var accepted = await RequestAsync(slots[0]);
        var booking = _store.Bookings[0];
        Assert.Equal(
            "Calendar.PublicBooking.NotVerified",
            (await ChooseAsync(accepted.Value.RequestHandle, slots[2])).Error.Code
        );
        _availability.Block(_ana, slots[0]);
        _availability.Block(_ben, slots[0]);
        await VerifyAsync(accepted.Value.RequestHandle, CodeOf(booking));
        _bus.Published.Clear();

        var chosen = await ChooseAsync(accepted.Value.RequestHandle, slots[2]);

        Assert.True(chosen.IsSuccess, chosen.IsFailure ? chosen.Error.Code : null);
        Assert.Equal(slots[2], booking.SlotStartUtc);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Choosing_or_resending_with_an_unknown_handle_answers_like_a_real_one()
    {
        var accepted = await RequestAsync((await SlotsAsync())[0]);

        Assert.Equal(
            "Calendar.PublicBooking.NotVerified",
            (await ChooseAsync("made-up", DateTime.UtcNow.AddDays(2))).Error.Code
        );
        Assert.True((await ResendAsync("made-up")).IsSuccess);
        for (var send = 0; send < PublicBooking.MaxCodeSends + 1; send++)
            Assert.True((await ResendAsync(accepted.Value.RequestHandle)).IsSuccess);
        Assert.Equal(
            PublicBooking.MaxCodeSends,
            _bus.Published.OfType<PublicBookingCodeRequestedIntegrationEvent>().Count()
        );
    }

    // ---------- Gestionar ----------

    /// <summary>
    /// Quien ya tiene una demo y pide otra recibe en pantalla la misma respuesta que cualquiera (anti-enumeración),
    /// pero al dueño del email le llega un aviso con su demo y un enlace de gestión nuevo (DEMO-11 U5). Uno por hora:
    /// nadie puede invalidar el enlace del visitante en cadena.
    /// </summary>
    [Fact]
    public async Task Asking_again_with_a_booked_demo_mails_its_owner_once_per_hour()
    {
        var slots = await SlotsAsync();
        var token = await ConfirmedTokenAsync(slots[0]);
        _bus.Published.Clear();

        var again = await RequestAsync(slots[2], key: "key-2");

        Assert.True(again.IsSuccess);
        Assert.Single(_store.Bookings);
        Assert.Empty(_bus.Published.OfType<PublicBookingCodeRequestedIntegrationEvent>());
        var notice = Assert.Single(_bus.Published.OfType<PublicBookingAlreadyBookedIntegrationEvent>());
        Assert.Equal(slots[0], notice.StartUtc);
        Assert.Equal("jane@acme-tax.com", notice.Visitor.Email);
        var fresh = _codec
            .Unprotect<PublicBookingSecretPayload>(
                notice.ProtectedManagementToken,
                PublicBookingPayloadSchemas.ManagementToken,
                PublicBookingPayloadSchemas.ManagementTokenBinding(
                    Tenant,
                    notice.BookingId,
                    notice.ManagementTokenVersion
                )
            )
            .Value;
        Assert.True((await ViewPublicBookingHandler.Handle(new(fresh), _store, _store, default)).IsSuccess);
        Assert.True((await ViewPublicBookingHandler.Handle(new(token), _store, _store, default)).IsFailure);

        _bus.Published.Clear();
        await RequestAsync(slots[3], key: "key-3");

        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task The_visitor_can_see_move_and_cancel_the_demo_with_the_management_link()
    {
        var slots = await SlotsAsync();
        var token = await ConfirmedTokenAsync(slots[0]);

        var view = await ViewPublicBookingHandler.Handle(new(token), _store, _store, default);
        Assert.Equal(slots[0], view.Value.StartUtc);
        Assert.True(view.Value.CanChange);

        var moved = await RescheduleAsync(token, slots[4]);
        Assert.True(moved.IsSuccess, moved.IsFailure ? moved.Error.Code : null);
        Assert.Equal(slots[4], _appointments.All[0].Timing.StartUtc);
        var rescheduled = Assert.Single(_bus.Published.OfType<PublicBookingRescheduledIntegrationEvent>());
        Assert.Equal(slots[0], rescheduled.PreviousStartUtc);
        Assert.Equal("Visitor", rescheduled.ChangedBy);
        Assert.Equal(
            "Calendar.PublicBooking.NotFound",
            (await ViewPublicBookingHandler.Handle(new(token), _store, _store, default)).Error.Code
        );

        var cancelled = await CancelPublicBookingHandler.Handle(
            new(moved.Value.ManagementToken, null),
            _store,
            _appointments,
            _unitOfWork,
            default
        );
        Assert.True(cancelled.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, _appointments.All[0].Status);
        Assert.Equal(PublicBookingStatus.Cancelled, _store.Bookings[0].Status);
    }

    [Fact]
    public async Task If_the_appointment_cannot_change_the_booking_does_not_change_either()
    {
        var token = await ConfirmedTokenAsync((await SlotsAsync())[0]);
        _appointments.All[0].CancelByExternalRequester(null, DateTime.UtcNow);

        var cancelled = await CancelPublicBookingHandler.Handle(
            new(token, null),
            _store,
            _appointments,
            _unitOfWork,
            default
        );
        var moved = await RescheduleAsync(token, (await SlotsAsync())[3]);

        Assert.Equal("Calendar.Appointment.CancelledIsFinal", cancelled.Error.Code);
        Assert.Equal("Calendar.Appointment.CancelledIsFinal", moved.Error.Code);
        Assert.Equal(PublicBookingStatus.Confirmed, _store.Bookings[0].Status);
        Assert.Equal(0, _store.Bookings[0].IcsSequence);
    }

    [Fact]
    public async Task A_bad_management_link_is_simply_not_found()
    {
        var view = await ViewPublicBookingHandler.Handle(new("nope"), _store, _store, default);
        var cancel = await CancelPublicBookingHandler.Handle(
            new("nope", null),
            _store,
            _appointments,
            _unitOfWork,
            default
        );

        Assert.Equal("Calendar.PublicBooking.NotFound", view.Error.Code);
        Assert.Equal("Calendar.PublicBooking.NotFound", cancel.Error.Code);
    }

    // ---------- Ayudas ----------

    private async Task<string> ConfirmedTokenAsync(DateTime slot)
    {
        var accepted = await RequestAsync(slot);
        var booking = _store.Bookings.Single(b =>
            b.RequestHandleHash.SequenceEqual(OpaqueToken.HashOf(accepted.Value.RequestHandle))
        );
        Assert.True((await VerifyAsync(accepted.Value.RequestHandle, CodeOf(booking))).IsSuccess);
        return TokenOf(booking);
    }

    /// <summary>El último código mandado a esa reserva, descifrado como lo haría Notification.</summary>
    private string CodeOf(PublicBooking booking)
    {
        var sent = _bus
            .Published.OfType<PublicBookingCodeRequestedIntegrationEvent>()
            .Last(e => e.BookingId == booking.Id);
        return _codec
            .Unprotect<PublicBookingSecretPayload>(
                sent.ProtectedCode,
                PublicBookingPayloadSchemas.VerificationCode,
                PublicBookingPayloadSchemas.CodeBinding(booking.TenantId, booking.Id, sent.Send)
            )
            .Value;
    }

    private string TokenOf(PublicBooking booking)
    {
        var sent = _bus.Published.OfType<PublicBookingConfirmedIntegrationEvent>().Last(e => e.BookingId == booking.Id);
        return _codec
            .Unprotect<PublicBookingSecretPayload>(
                sent.ProtectedManagementToken,
                PublicBookingPayloadSchemas.ManagementToken,
                PublicBookingPayloadSchemas.ManagementTokenBinding(
                    booking.TenantId,
                    booking.Id,
                    sent.ManagementTokenVersion
                )
            )
            .Value;
    }

    private Task<Result<PublicBookingPageResponse>> PageAsync(string language) =>
        GetPublicBookingPageHandler.Handle(new("demo", language), _store, Scheduler(), _bot, _options, default);

    private async Task<List<DateTime>> SlotsAsync()
    {
        var from = DateTime.UtcNow;
        var result = await GetPublicBookingSlotsHandler.Handle(
            new("demo", from, from.AddDays(7)),
            _store,
            Scheduler(),
            _options,
            default
        );
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return [.. result.Value.Slots.Select(slot => slot.StartUtc)];
    }

    private Task<Result<PublicBookingRequestAccepted>> RequestAsync(
        DateTime slot,
        string email = "jane@acme-tax.com",
        string key = "key-1",
        OfficeSize? officeSize = OfficeSize.TwoToFive,
        string heardAbout = "whatsapp"
    ) =>
        RequestPublicBookingHandler.Handle(
            new RequestPublicBookingCommand(
                "demo",
                key,
                _bot.Ready && _bot.Passes,
                new PublicBookingForm(
                    slot,
                    "Jane Doe",
                    email,
                    "Acme Tax",
                    officeSize,
                    null,
                    null,
                    heardAbout,
                    null,
                    BookingEntrySource.Hero,
                    "google",
                    "cpc",
                    "fall",
                    "google.com",
                    BookingLanguage.En,
                    "America/New_York",
                    false,
                    "2026-11-demo-v1"
                )
            ),
            _store,
            _store,
            Scheduler(),
            Notices(),
            _bot,
            new TestRequesterKeyHasher(),
            _options,
            _unitOfWork,
            _bus,
            default
        );

    private Task<Result<PublicBookingConfirmation>> VerifyAsync(string handle, string code) =>
        VerifyPublicBookingHandler.Handle(
            new(handle, code),
            _store,
            _store,
            Scheduler(),
            Notices(),
            _unitOfWork,
            _bus,
            default
        );

    private Task<Result> ChooseAsync(string handle, DateTime slot) =>
        ChoosePublicBookingSlotHandler.Handle(new(handle, slot), _store, _store, Scheduler(), _unitOfWork, default);

    private Task<Result> ResendAsync(string handle) =>
        ResendPublicBookingCodeHandler.Handle(new(handle), _store, _store, Notices(), _unitOfWork, _bus, default);

    private Task<Result<PublicBookingRescheduled>> RescheduleAsync(string token, DateTime slot) =>
        ReschedulePublicBookingHandler.Handle(
            new(token, slot),
            _store,
            _store,
            _appointments,
            Scheduler(),
            Notices(),
            _unitOfWork,
            _bus,
            default
        );

    private PublicBookingScheduler Scheduler() =>
        new(_appointments, _availability, Types(), new NoOfficeSchedule(), new NoScheduleLock(), _store, _unitOfWork);

    private PublicBookingNotices Notices() => new(_availability, _codec, new FixedCorrelation());

    private SingleTypeRepository Types() => new(_type);
}
