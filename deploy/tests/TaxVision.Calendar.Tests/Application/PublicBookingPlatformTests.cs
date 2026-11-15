using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Application.PublicBookings.Consumers;
using TaxVision.Calendar.Application.PublicBookings.Platform;
using TaxVision.Calendar.Application.Staff.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.PublicBookings.Events;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Tests.Domain;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>Lo que hace el equipo de la plataforma: configurar la página y atender las reservas (cita incluida).</summary>
public sealed class PublicBookingPlatformTests
{
    private static readonly Guid Tenant = PlatformTenant.Id;
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _ana = new("00000000-0000-0000-0000-00000000000a");
    private readonly Guid _ben = new("00000000-0000-0000-0000-00000000000b");
    private readonly FakeMessageBus _bus = new();
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
    private readonly AppointmentType _type = AppointmentType
        .Create(Tenant, "Product demo", TimeSpan.FromMinutes(30), "#2563EB", DateTime.UtcNow, blocksOnConflict: true)
        .Value;
    private readonly PublicBookingStore _store = new();
    private readonly InMemoryAppointments _appointments = new();
    private readonly NineToFiveAvailability _availability = new();
    private readonly Contacts _contacts = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly IOptions<PublicBookingOptions> _options = Options.Create(new PublicBookingOptions());

    public PublicBookingPlatformTests()
    {
        _availability.Add(_ana);
        _availability.Add(_ben);
        _contacts.Known[_ana] = new StaffContact("ana@taxoffix.io", "Ana Pérez", IsActive: true);
        _contacts.Known[_ben] = new StaffContact("ben@taxoffix.io", "Ben Ortiz", IsActive: true);
    }

    // ---------- Página ----------

    [Fact]
    public async Task A_page_is_created_inactive_in_the_platform_tenant()
    {
        var created = await CreateAsync();

        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);
        Assert.False(created.Value.IsActive);
        Assert.Equal("demo", created.Value.Slug);
        Assert.Equal("Agenda una demo en vivo", created.Value.TitleEs);
        Assert.Single(_store.Pages);
    }

    [Fact]
    public async Task An_office_tenant_cannot_publish_a_page()
    {
        var created = await CreateAsync(tenant: Guid.NewGuid());

        Assert.Equal("Calendar.BookingPage.TenantNotAllowed", created.Error.Code);
        Assert.Empty(_store.Pages);
    }

    [Fact]
    public async Task The_slug_and_the_type_are_checked_before_creating()
    {
        await CreateAsync();

        Assert.Equal("Calendar.BookingPage.SlugTaken", (await CreateAsync()).Error.Code);
        Assert.Equal(
            "Calendar.BookingPage.TypeNotUsable",
            (await CreateAsync(slug: "other", virtualType: true)).Error.Code
        );
    }

    [Fact]
    public async Task Hosts_must_be_active_users_of_the_platform()
    {
        var page = (await CreateAsync()).Value;
        var stranger = Guid.NewGuid();
        var gone = Guid.NewGuid();
        _contacts.Known[gone] = new StaffContact("gone@taxoffix.io", "Gone", IsActive: false);

        Assert.Equal("Calendar.BookingPage.HostNotFound", (await SetHostsAsync(page.Id, stranger)).Error.Code);
        Assert.Equal("Calendar.BookingPage.HostInactive", (await SetHostsAsync(page.Id, gone)).Error.Code);
        var saved = await SetHostsAsync(page.Id, _ana);
        Assert.True(saved.IsSuccess);
        Assert.Equal(_ana, Assert.Single(saved.Value.Hosts).UserId);
    }

    [Fact]
    public async Task A_complete_page_can_be_activated_and_listed()
    {
        var page = (await CreateAsync()).Value;
        await SetHostsAsync(page.Id, _ana);
        await SetHeardAboutOptionsHandler.Handle(
            new SetBookingPageHeardAboutOptionsCommand(Tenant, _admin, page.Id, [BookingPageTests.Option("whatsapp")]),
            _store,
            _unitOfWork,
            default
        );
        await ChangeBookingPageConsentHandler.Handle(
            new ChangeBookingPageConsentCommand(Tenant, _admin, page.Id, "Privacy", null, "Marketing", null, "v2"),
            _store,
            _unitOfWork,
            default
        );

        var activated = await ActivateBookingPageHandler.Handle(
            new ActivateBookingPageCommand(Tenant, _admin, page.Id),
            _store,
            Types(),
            _unitOfWork,
            default
        );
        var list = await ListBookingPagesHandler.Handle(new ListBookingPagesQuery(Tenant), _store, default);

        Assert.True(activated.IsSuccess, activated.IsFailure ? activated.Error.Code : null);
        Assert.True(activated.Value.IsActive);
        Assert.Equal("v2", activated.Value.ConsentVersion);
        Assert.True(Assert.Single(list.Value).IsActive);
    }

    [Fact]
    public async Task Rules_and_fields_are_saved_with_who_changed_them()
    {
        var page = (await CreateAsync()).Value;
        var other = Guid.NewGuid();

        var rules = await ChangeBookingPageRulesHandler.Handle(
            new ChangeBookingPageRulesCommand(Tenant, other, page.Id, 120, 45, 10, 15, false, null, [60]),
            _store,
            _unitOfWork,
            default
        );
        var fields = await ConfigureBookingPageFieldsHandler.Handle(
            new ConfigureBookingPageFieldsCommand(
                Tenant,
                other,
                page.Id,
                BookingFieldMode.Hidden,
                BookingFieldMode.Required,
                BookingFieldMode.Optional
            ),
            _store,
            _unitOfWork,
            default
        );

        Assert.True(rules.IsSuccess);
        Assert.Equal(15, rules.Value.SlotStepMinutes);
        Assert.False(rules.Value.RequireEmailVerification);
        Assert.Equal("Required", fields.Value.OfficeSizeField);
        Assert.Equal(other, _store.Pages[0].UpdatedByUserId);
    }

    [Fact]
    public async Task A_page_of_another_tenant_does_not_exist()
    {
        var page = (await CreateAsync()).Value;

        var found = await GetBookingPageHandler.Handle(
            new GetBookingPageQuery(Guid.NewGuid(), page.Id),
            _store,
            default
        );

        Assert.Equal("Calendar.BookingPage.NotFound", found.Error.Code);
    }

    // ---------- Reservas ----------

    [Fact]
    public async Task The_team_cancels_a_demo_and_its_appointment_together()
    {
        var booking = await ConfirmedBookingAsync();

        var cancelled = await StaffCancelPublicBookingHandler.Handle(
            new StaffCancelPublicBookingCommand(Tenant, _admin, booking.Id, "Host is sick"),
            _store,
            _store,
            _appointments,
            _unitOfWork,
            default
        );

        Assert.True(cancelled.IsSuccess, cancelled.IsFailure ? cancelled.Error.Code : null);
        Assert.Equal("Cancelled", cancelled.Value.Status);
        Assert.Equal(_admin, booking.CancelledByUserId);
        Assert.Equal(PublicBookingActor.Staff, booking.CancelledBy);
        Assert.Equal(AppointmentStatus.Cancelled, _appointments.All[0].Status);
    }

    [Fact]
    public async Task The_team_moves_a_demo_and_the_visitor_gets_a_new_link()
    {
        var booking = await ConfirmedBookingAsync();
        var later = (await SlotsAsync())[3];

        var moved = await StaffReschedulePublicBookingHandler.Handle(
            new StaffReschedulePublicBookingCommand(Tenant, _admin, booking.Id, later, "Host conflict"),
            _store,
            _store,
            _appointments,
            Scheduler(),
            Notices(),
            _unitOfWork,
            _bus,
            default
        );

        Assert.True(moved.IsSuccess, moved.IsFailure ? moved.Error.Code : null);
        Assert.Equal(later, moved.Value.SlotStartUtc);
        Assert.Equal(later, _appointments.All[0].Timing.StartUtc);
        var evt = Assert.Single(_bus.Published.OfType<PublicBookingRescheduledIntegrationEvent>());
        Assert.Equal("Staff", evt.ChangedBy);
        Assert.Equal("Host conflict", evt.Reason);
        Assert.Empty(booking.DomainEvents.OfType<PublicBookingCancelledDomainEvent>());
    }

    [Fact]
    public async Task The_outcome_waits_until_the_demo_is_over()
    {
        var booking = await ConfirmedBookingAsync();

        var early = await MarkPublicBookingOutcomeHandler.Handle(
            new MarkPublicBookingOutcomeCommand(Tenant, _admin, booking.Id, PublicBookingOutcome.Completed),
            _store,
            _store,
            _unitOfWork,
            default
        );

        Assert.Equal("Calendar.PublicBooking.NotFinishedYet", early.Error.Code);
    }

    [Fact]
    public async Task A_booking_of_another_tenant_does_not_exist()
    {
        var booking = await ConfirmedBookingAsync();

        var cancelled = await StaffCancelPublicBookingHandler.Handle(
            new StaffCancelPublicBookingCommand(Guid.NewGuid(), _admin, booking.Id, null),
            _store,
            _store,
            _appointments,
            _unitOfWork,
            default
        );

        Assert.Equal("Calendar.PublicBooking.NotFound", cancelled.Error.Code);
    }

    // ---------- Retiro de un anfitrión (guardrail 38) ----------

    [Fact]
    public async Task When_a_host_leaves_their_demo_goes_to_another_free_host_with_its_link()
    {
        var booking = await ConfirmedBookingAsync(withBen: true);
        _bus.Published.Clear();

        await OffboardAsync(_ana);

        Assert.Equal(_ben, booking.HostUserId);
        Assert.Equal(_ben, _appointments.All[0].OrganizerUserId);
        Assert.Equal("https://zoom.us/j/1", _appointments.All[0].Location!.Value);
        Assert.Null(_store.Pages[0].FindActiveHost(_ana));
        Assert.True(_store.Pages[0].IsActive);
        var evt = Assert.Single(_bus.Published.OfType<PublicBookingRescheduledIntegrationEvent>());
        Assert.Equal("https://zoom.us/j/1", evt.Host.MeetingUrl);
        Assert.Equal(booking.SlotStartUtc, evt.PreviousStartUtc);
    }

    [Fact]
    public async Task When_the_only_host_leaves_the_demo_is_cancelled_and_the_page_switched_off()
    {
        var booking = await ConfirmedBookingAsync();

        await OffboardAsync(_ana);

        Assert.Equal(PublicBookingStatus.Cancelled, booking.Status);
        Assert.Equal(AppointmentStatus.Cancelled, _appointments.All[0].Status);
        Assert.False(_store.Pages[0].IsActive);
    }

    [Fact]
    public async Task Verifying_hosts_happens_outside_the_save()
    {
        var stranger = Guid.NewGuid();

        var verified = await VerifyPlatformHostsHandler.Handle(
            new VerifyPlatformHostsQuery(Tenant, [_ana, stranger]),
            _contacts,
            default
        );

        Assert.Equal("Calendar.BookingPage.HostNotFound", verified.Error.Code);
        Assert.Equal(0, _unitOfWork.Saves);
    }

    // ---------- Ayudas ----------

    private Task OffboardAsync(Guid userId) =>
        PublicBookingHostOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = userId,
                Email = "ana@taxoffix.io",
                ActorType = "PlatformAdmin",
                OffboardedByUserId = _admin,
                RemovedAtUtc = DateTime.UtcNow,
            },
            _store,
            _store,
            _appointments,
            Scheduler(),
            Notices(),
            _unitOfWork,
            _bus,
            NullLogger<PublicBooking>.Instance,
            default
        );

    private Task<BuildingBlocks.Results.Result<BookingPageAdminView>> CreateAsync(
        Guid? tenant = null,
        string slug = "demo",
        bool virtualType = false
    ) =>
        CreateBookingPageHandler.Handle(
            new CreateBookingPageCommand(
                tenant ?? Tenant,
                _admin,
                slug,
                "Book a live demo",
                "Agenda una demo en vivo",
                "See it live.",
                null,
                _type.Id
            ),
            _store,
            virtualType ? VirtualTypes() : Types(),
            _options,
            _unitOfWork,
            default
        );

    /// <summary>Como el controlador: primero se verifican en Auth (sin base) y después se guardan.</summary>
    private async Task<BuildingBlocks.Results.Result<BookingPageAdminView>> SetHostsAsync(
        Guid pageId,
        params BookingPageHostInput[] hosts
    )
    {
        var verified = await VerifyPlatformHostsHandler.Handle(
            new VerifyPlatformHostsQuery(Tenant, [.. hosts.Select(h => h.UserId)]),
            _contacts,
            default
        );
        if (verified.IsFailure)
            return BuildingBlocks.Results.Result.Failure<BookingPageAdminView>(verified.Error);
        return await SetBookingPageHostsHandler.Handle(
            new SetBookingPageHostsCommand(Tenant, _admin, pageId, hosts),
            _store,
            _unitOfWork,
            default
        );
    }

    private Task<BuildingBlocks.Results.Result<BookingPageAdminView>> SetHostsAsync(Guid pageId, Guid userId) =>
        SetHostsAsync(pageId, BookingPageTests.Host(userId));

    /// <summary>Una reserva confirmada con su cita, armada por el mismo camino que el visitante.</summary>
    private async Task<PublicBooking> ConfirmedBookingAsync(bool withBen = false)
    {
        var page = (await CreateAsync()).Value;
        if (withBen)
            await SetHostsAsync(
                page.Id,
                BookingPageTests.Host(_ana, order: 0),
                BookingPageTests.Host(_ben, order: 1) with
                {
                    MeetingUrl = "https://zoom.us/j/1",
                }
            );
        else
            await SetHostsAsync(page.Id, _ana);
        var stored = _store.Pages[0];
        stored.SetHeardAboutOptions([BookingPageTests.Option("whatsapp")], _admin, DateTime.UtcNow);
        stored.ChangeConsent(
            BookingPageTests.Privacy,
            BookingPageTests.Marketing,
            "2026-11-demo-v1",
            _admin,
            DateTime.UtcNow
        );
        Assert.True(stored.Activate(_admin, DateTime.UtcNow).IsSuccess);

        var slot = (await SlotsAsync())[0];
        var issued = PublicBooking
            .Request(Tenant, stored.Id, PublicBookingTests.Data(), slot, 30, _ana, "key-1", false, 10, DateTime.UtcNow)
            .Value;
        _store.Add(issued.Booking);
        Assert.True((await Scheduler().ConfirmAsync(stored, issued.Booking, DateTime.UtcNow, default)).IsSuccess);
        return issued.Booking;
    }

    private async Task<List<DateTime>> SlotsAsync()
    {
        var page = _store.Pages[0];
        var type = await Scheduler().TypeOfAsync(page, default);
        var from = DateTime.UtcNow;
        return [.. await Scheduler().ListStartsAsync(page, type!, from, from.AddDays(7), from, default)];
    }

    private PublicBookingScheduler Scheduler() =>
        new(_appointments, _availability, Types(), new NoOfficeSchedule(), new NoScheduleLock(), _store, _unitOfWork);

    private PublicBookingNotices Notices() => new(_availability, _codec, new FixedCorrelation());

    private SingleTypeRepository Types() => new(_type);

    private SingleTypeRepository VirtualTypes() =>
        new(
            AppointmentType
                .Create(Tenant, "Video", TimeSpan.FromMinutes(30), "#2563EB", DateTime.UtcNow, isVirtual: true)
                .Value
        );

    private sealed class Contacts : IStaffContactClient
    {
        public Dictionary<Guid, StaffContact> Known { get; } = [];

        public Task<StaffContact?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Known.GetValueOrDefault(userId));
    }
}
