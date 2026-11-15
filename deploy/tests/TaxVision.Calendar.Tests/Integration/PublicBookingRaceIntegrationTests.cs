using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using TaxVision.Calendar.Tests.Domain;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// Visitantes que piden el mismo horario a la vez, contra SQL real (base temporal BB-10) con el candado y los
/// índices de verdad: nunca quedan más retenciones que anfitriones libres.
/// </summary>
public sealed class PublicBookingRaceIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly List<Guid> _hosts = [];
    private IOptions<PublicBookingOptions> _options = null!;

    public Task InitializeAsync()
    {
        _options = Options.Create(new PublicBookingOptions { AllowedTenantIds = [_tenant] });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.PublicBookings.IgnoreQueryFilters().Where(b => b.TenantId == _tenant).ExecuteDeleteAsync();
        await context.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == _tenant).ExecuteDeleteAsync();
        await context.BookingPages.IgnoreQueryFilters().Where(p => p.TenantId == _tenant).ExecuteDeleteAsync();
        await context.AvailabilityRules.IgnoreQueryFilters().Where(r => r.TenantId == _tenant).ExecuteDeleteAsync();
        await context.AppointmentTypes.IgnoreQueryFilters().Where(t => t.TenantId == _tenant).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Two_visitors_and_one_host_leave_a_single_hold()
    {
        var slot = await SeedAsync(hosts: 1);

        var outcomes = await Task.WhenAll(RequestAsync(slot, "a@acme-tax.com"), RequestAsync(slot, "b@acme-tax.com"));

        Assert.Equal(1, outcomes.Count(ok => ok));
        Assert.Equal(1, await HoldingCountAsync(slot));
    }

    [Fact]
    public async Task Three_visitors_and_two_hosts_leave_two_holds_with_different_hosts()
    {
        var slot = await SeedAsync(hosts: 2);

        var outcomes = await Task.WhenAll(
            RequestAsync(slot, "a@acme-tax.com"),
            RequestAsync(slot, "b@acme-tax.com"),
            RequestAsync(slot, "c@acme-tax.com")
        );

        Assert.Equal(2, outcomes.Count(ok => ok));
        await using var check = CreateContext();
        var hosts = await check
            .PublicBookings.IgnoreQueryFilters()
            .Where(b =>
                b.TenantId == _tenant && b.SlotStartUtc == slot && b.Status == PublicBookingStatus.PendingVerification
            )
            .Select(b => b.HostUserId)
            .ToListAsync();
        Assert.Equal(2, hosts.Distinct().Count());
    }

    [Fact]
    public async Task An_abandoned_hold_does_not_block_the_next_visitor_against_the_unique_index()
    {
        var slot = await SeedAsync(hosts: 1);
        await using (var write = CreateContext())
        {
            var page = await write.BookingPages.IgnoreQueryFilters().SingleAsync(p => p.TenantId == _tenant);
            var abandoned = PublicBooking
                .Request(
                    _tenant,
                    page.Id,
                    PublicBookingTests.Data("gone@acme-tax.com"),
                    slot,
                    30,
                    _hosts[0],
                    "old",
                    true,
                    10,
                    DateTime.UtcNow.AddMinutes(-20)
                )
                .Value.Booking;
            write.PublicBookings.Add(abandoned);
            await write.SaveChangesAsync();
        }

        Assert.True(await RequestAsync(slot, "next@acme-tax.com"));
        Assert.Equal(1, await HoldingCountAsync(slot));
    }

    [Fact]
    public async Task Verifying_a_hold_confirms_it_and_creates_the_appointment()
    {
        var slot = await SeedAsync(hosts: 1);
        Assert.True(await RequestAsync(slot, "a@acme-tax.com"));
        var (handle, code) = await RenewHandleAsync();

        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var confirmed = await VerifyPublicBookingHandler.Handle(
            new VerifyPublicBookingCommand(handle, code),
            new PublicBookingRepository(context),
            new BookingPageRepository(context),
            Scheduler(context),
            Notices(context),
            context,
            new Application.FakeMessageBus(),
            default
        );
        await transaction.CommitAsync();

        Assert.True(confirmed.IsSuccess, confirmed.IsFailure ? confirmed.Error.Code : null);
        await using var check = CreateContext();
        var appointment = await check.Appointments.IgnoreQueryFilters().SingleAsync(a => a.TenantId == _tenant);
        Assert.NotNull(appointment.BookingPageId);
        Assert.Equal(slot, appointment.Timing.StartUtc);
    }

    /// <summary>Página activa con N anfitriones de 9 a 17 (Nueva York); devuelve el primer horario ofrecido.</summary>
    private async Task<DateTime> SeedAsync(int hosts)
    {
        await using var context = CreateContext();
        var type = AppointmentType
            .Create(
                _tenant,
                "Product demo",
                TimeSpan.FromMinutes(30),
                "#2563EB",
                DateTime.UtcNow,
                blocksOnConflict: true
            )
            .Value;
        context.AppointmentTypes.Add(type);

        var page = BookingPageTests.NewPage();
        page.SetTenant(_tenant);
        page.ChangeAppointmentType(type, _admin, DateTime.UtcNow);
        for (var i = 0; i < hosts; i++)
        {
            var host = Guid.NewGuid();
            _hosts.Add(host);
            context.AvailabilityRules.Add(
                AvailabilityRule
                    .Create(
                        _tenant,
                        host,
                        DaysOfWeekMask.EveryDay,
                        new TimeOnly(9, 0),
                        new TimeOnly(17, 0),
                        "America/New_York",
                        DateTime.UtcNow
                    )
                    .Value
            );
        }
        page.SetHosts([.. _hosts.Select((host, order) => BookingPageTests.Host(host, order))], _admin, DateTime.UtcNow);
        page.SetHeardAboutOptions([BookingPageTests.Option("whatsapp")], _admin, DateTime.UtcNow);
        page.ChangeConsent(
            BookingPageTests.Privacy,
            BookingPageTests.Marketing,
            "2026-11-demo-v1",
            _admin,
            DateTime.UtcNow
        );
        Assert.True(page.Activate(_admin, DateTime.UtcNow).IsSuccess);
        context.BookingPages.Add(page);
        await context.SaveChangesAsync();

        var from = DateTime.UtcNow;
        var slots = await GetPublicBookingSlotsHandler.Handle(
            new GetPublicBookingSlotsQuery("demo", from, from.AddDays(7)),
            new BookingPageRepository(context),
            Scheduler(context),
            _options,
            default
        );
        return slots.Value.Slots[0].StartUtc;
    }

    /// <summary>Una solicitud completa en su propia transacción, como la corre Wolverine. True si retuvo el horario.</summary>
    private async Task<bool> RequestAsync(DateTime slot, string email)
    {
        await using var context = CreateContext();
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var result = await RequestPublicBookingHandler.Handle(
                new RequestPublicBookingCommand("demo", $"key-{email}", true, Form(slot, email)),
                new BookingPageRepository(context),
                new PublicBookingRepository(context),
                Scheduler(context),
                Notices(context),
                new PassingBot(),
                new Application.TestRequesterKeyHasher(),
                _options,
                context,
                new Application.FakeMessageBus(),
                default
            );
            await transaction.CommitAsync();
            return result.IsSuccess;
        }
        catch (ConflictException)
        {
            return false;
        }
    }

    /// <summary>El código en claro sale una sola vez: se emite uno nuevo (y un manejo nuevo) para la prueba.</summary>
    private async Task<(string Handle, string Code)> RenewHandleAsync()
    {
        await using var context = CreateContext();
        var booking = await context.PublicBookings.IgnoreQueryFilters().SingleAsync(b => b.TenantId == _tenant);
        var code = booking.ResendCode(DateTime.UtcNow).Value;
        var handle = booking.ReissueRequestHandle(DateTime.UtcNow).Value;
        await context.SaveChangesAsync();
        return (handle, code);
    }

    private async Task<int> HoldingCountAsync(DateTime slot)
    {
        await using var context = CreateContext();
        return await context
            .PublicBookings.IgnoreQueryFilters()
            .CountAsync(b =>
                b.TenantId == _tenant && b.SlotStartUtc == slot && b.Status == PublicBookingStatus.PendingVerification
            );
    }

    private static PublicBookingForm Form(DateTime slot, string email) =>
        new(
            slot,
            "Jane Doe",
            email,
            "Acme Tax",
            OfficeSize.TwoToFive,
            null,
            null,
            "whatsapp",
            null,
            BookingEntrySource.Hero,
            null,
            null,
            null,
            null,
            BookingLanguage.En,
            "America/New_York",
            false,
            "2026-11-demo-v1"
        );

    private static PublicBookingScheduler Scheduler(CalendarDbContext context) =>
        new(
            new AppointmentRepository(context),
            new AvailabilityRepository(context),
            new AppointmentTypeRepository(context),
            new OfficeScheduleRepository(context),
            new SqlOrganizerScheduleLock(context, Options.Create(new OrganizerScheduleLockOptions())),
            new PublicBookingRepository(context),
            context
        );

    private static PublicBookingNotices Notices(CalendarDbContext context) =>
        new(
            new AvailabilityRepository(context),
            new JsonProtectedPayloadCodec(
                new AesGcmRotatingSecretProtector(
                    Options.Create(
                        new RotatingSecretProtectionOptions
                        {
                            MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                            MasterKeyVersion = 1,
                        }
                    )
                )
            ),
            new Application.FixedCorrelation()
        );

    private CalendarDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<CalendarDbContext>()
                .UseSqlServer(CalendarIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );

    private sealed class PassingBot : IPublicBookingBotCheck
    {
        public bool IsReady => true;
        public string? SiteKey => "site-key";

        public Task<bool> PassesAsync(string? token, string? clientIp, CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    // Como el visitante anónimo: sin tenant en contexto; los repositorios lo llevan explícito.
    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid value) { }
    }
}
