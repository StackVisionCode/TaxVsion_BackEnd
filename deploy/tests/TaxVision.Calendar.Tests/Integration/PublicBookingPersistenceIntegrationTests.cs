using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using TaxVision.Calendar.Tests.Domain;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// La página pública y sus reservas contra SQL real (base temporal BB-10): los índices únicos son la última
/// defensa contra el doble horario y el doble envío. Limpia su tenant al final.
/// </summary>
public sealed class PublicBookingPersistenceIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Slot = new(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _host = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.PublicBookings.IgnoreQueryFilters().Where(b => b.TenantId == _tenant).ExecuteDeleteAsync();
        await context.BookingPages.IgnoreQueryFilters().Where(p => p.TenantId == _tenant).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task A_page_round_trips_with_its_texts_hosts_and_options()
    {
        var page = await SavePageAsync("demo");

        await using var read = CreateContext();
        var loaded = await read
            .BookingPages.Include(p => p.Hosts)
            .Include(p => p.HeardAboutOptions)
            .SingleAsync(p => p.Id == page.Id);

        Assert.Equal("demo", loaded.Slug.Value);
        Assert.Equal("Agenda una demo en vivo", loaded.Title.For(BookingLanguage.Es));
        Assert.Equal("See it live.", loaded.Description.For(BookingLanguage.Es));
        Assert.Equal([1440, 60], loaded.IcsAlarmMinutes);
        Assert.Equal(_host, Assert.Single(loaded.ActiveHosts).UserId);
        Assert.True(loaded.FindActiveOption("other")!.AllowsDetail);
        Assert.True(loaded.IsActive);
        Assert.Equal(BookingPageTests.Privacy.En, loaded.PrivacyNotice!.En);
        Assert.Null(loaded.MarketingConsent!.Es);
    }

    [Fact]
    public async Task Replacing_the_hosts_deletes_the_removed_ones()
    {
        var page = await SavePageAsync("demo");
        var other = Guid.NewGuid();

        await using (var write = CreateContext())
        {
            var tracked = await write.BookingPages.Include(p => p.Hosts).SingleAsync(p => p.Id == page.Id);
            Assert.True(tracked.SetHosts([BookingPageTests.Host(other)], _admin, Now).IsSuccess);
            await write.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var hosts = await read.BookingPages.Where(p => p.Id == page.Id).SelectMany(p => p.Hosts).ToListAsync();
        Assert.Equal(other, Assert.Single(hosts).UserId);
    }

    [Fact]
    public async Task The_slug_is_unique_per_tenant()
    {
        await SavePageAsync("demo");

        await Assert.ThrowsAsync<ConflictException>(() => SavePageAsync("demo"));
    }

    [Fact]
    public async Task Two_visitors_cannot_hold_the_same_slot_of_the_same_host()
    {
        var page = await SavePageAsync("demo");
        var first = await SaveBookingAsync(page.Id, "key-1", "first@acme-tax.com");

        await Assert.ThrowsAsync<ConflictException>(() => SaveBookingAsync(page.Id, "key-2", "second@acme-tax.com"));

        // Vencida la retención, el horario vuelve a estar libre.
        await using (var write = CreateContext())
        {
            var tracked = await write.PublicBookings.SingleAsync(b => b.Id == first.Id);
            Assert.True(tracked.Expire(Now.AddMinutes(11)).IsSuccess);
            await write.SaveChangesAsync();
        }
        await SaveBookingAsync(page.Id, "key-3", "third@acme-tax.com");
    }

    [Fact]
    public async Task The_same_idempotency_key_cannot_create_two_requests()
    {
        var page = await SavePageAsync("demo");
        await SaveBookingAsync(page.Id, "same-key", "jane@acme-tax.com");

        await Assert.ThrowsAsync<ConflictException>(() =>
            SaveBookingAsync(page.Id, "same-key", "jane@acme-tax.com", Slot.AddHours(2))
        );
    }

    [Fact]
    public async Task A_visitor_has_one_active_booking_per_page()
    {
        var page = await SavePageAsync("demo");
        await SaveBookingAsync(page.Id, "key-1", "jane@acme-tax.com");

        await Assert.ThrowsAsync<ConflictException>(() =>
            SaveBookingAsync(page.Id, "key-2", "JANE@acme-tax.com", Slot.AddHours(2))
        );
    }

    [Fact]
    public async Task The_summary_counts_in_sql_by_status_answer_and_source()
    {
        var page = await SavePageAsync("demo");
        await SaveBookingAsync(page.Id, "k1", "a@acme-tax.com", Slot, verify: true);
        await SaveBookingAsync(page.Id, "k2", "b@acme-tax.com", Slot.AddHours(1));
        await SaveBookingAsync(page.Id, "k3", "c@acme-tax.com", Slot.AddDays(40));

        await using var read = CreateContext();
        var counts = await new PublicBookingRepository(read).CountAsync(
            new PublicBookingFilter(_tenant, page.Id, Slot.AddDays(-1), Slot.AddDays(1))
        );

        Assert.Equal(1, counts.ByStatus[PublicBookingStatus.Confirmed]);
        Assert.Equal(1, counts.ByStatus[PublicBookingStatus.PendingVerification]);
        Assert.Equal(2, counts.ByHeardAbout["whatsapp"]);
        Assert.Equal(2, counts.BySource[BookingEntrySource.Hero]);
        Assert.Equal(2, counts.ByUtmSource["google"]);
    }

    [Fact]
    public async Task The_list_filters_by_status_and_pages_in_slot_order()
    {
        var page = await SavePageAsync("demo");
        await SaveBookingAsync(page.Id, "k1", "a@acme-tax.com", Slot.AddHours(2), verify: true);
        await SaveBookingAsync(page.Id, "k2", "b@acme-tax.com", Slot.AddHours(1));
        await SaveBookingAsync(page.Id, "k3", "c@acme-tax.com", Slot);

        await using var read = CreateContext();
        var repository = new PublicBookingRepository(read);
        var range = new PublicBookingFilter(_tenant, page.Id, Slot.AddDays(-1), Slot.AddDays(1));
        var (firstPage, total) = await repository.ListAsync(range, page: 1, pageSize: 2);
        var (pending, pendingTotal) = await repository.ListAsync(
            range with
            {
                Status = PublicBookingStatus.PendingVerification,
            },
            page: 1,
            pageSize: 10
        );

        Assert.Equal(3, total);
        Assert.Equal([Slot, Slot.AddHours(1)], firstPage.Select(b => b.SlotStartUtc));
        Assert.Equal(2, pendingTotal);
        Assert.All(pending, b => Assert.Equal(PublicBookingStatus.PendingVerification, b.Status));
    }

    [Fact]
    public async Task A_confirmed_booking_keeps_only_hashes_of_its_secrets()
    {
        var page = await SavePageAsync("demo");
        var booking = await SaveBookingAsync(page.Id, "key-1", "jane@acme-tax.com", verify: true);

        await using var read = CreateContext();
        var loaded = await read.PublicBookings.SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(PublicBookingStatus.Confirmed, loaded.Status);
        Assert.Equal(32, loaded.ManagementTokenHash!.Length);
        Assert.Equal(32, loaded.RequestHandleHash.Length);
        Assert.Null(loaded.CodeHash);
        Assert.Equal(DateTimeKind.Utc, loaded.SlotStartUtc.Kind);
    }

    private async Task<BookingPage> SavePageAsync(string slug)
    {
        var page = BookingPage
            .Create(
                _tenant,
                BookingPageSlug.Create(slug).Value,
                LocalizedText.Create("Book a live demo", "Agenda una demo en vivo", BookingPage.MaxTitleLength).Value,
                LocalizedText.Create("See it live.", null, BookingPage.MaxDescriptionLength).Value,
                BookingPageTests.DemoType(_tenant),
                _admin,
                Now
            )
            .Value;
        page.SetHosts([BookingPageTests.Host(_host)], _admin, Now);
        page.SetHeardAboutOptions(
            [BookingPageTests.Option("google_search"), BookingPageTests.Option("other", allowsDetail: true)],
            _admin,
            Now
        );
        page.ChangeConsent(BookingPageTests.Privacy, BookingPageTests.Marketing, "2026-11-demo-v1", _admin, Now);
        Assert.True(page.Activate(_admin, Now).IsSuccess);

        await using var write = CreateContext();
        write.BookingPages.Add(page);
        await write.SaveChangesAsync();
        return page;
    }

    private async Task<PublicBooking> SaveBookingAsync(
        Guid pageId,
        string key,
        string email,
        DateTime? slot = null,
        bool verify = false
    )
    {
        var issued = PublicBooking
            .Request(_tenant, pageId, PublicBookingTests.Data(email), slot ?? Slot, 30, _host, key, true, 10, Now)
            .Value;
        if (verify)
        {
            Assert.True(issued.Booking.VerifyCode(issued.Code!, Now).IsSuccess);
            Assert.True(issued.Booking.Confirm(_host, Guid.NewGuid(), Now).IsSuccess);
        }

        await using var write = CreateContext();
        write.PublicBookings.Add(issued.Booking);
        await write.SaveChangesAsync();
        return issued.Booking;
    }

    private CalendarDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CalendarDbContext>()
            .UseSqlServer(CalendarIntegrationDatabase.ConnectionString)
            .Options;
        return new CalendarDbContext(options, new FixedTenant(_tenant));
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;

        public bool HasTenant => true;

        public void SetTenant(Guid value) { }
    }
}
