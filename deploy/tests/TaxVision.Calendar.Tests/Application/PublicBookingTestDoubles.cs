using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Tenancy;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Office;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Tests.Application;

// Dobles en memoria de la reserva pública, compartidos por las pruebas del visitante y de la plataforma.

internal sealed class FakePublicBookingBot : IPublicBookingBotCheck
{
    public bool Ready { get; set; } = true;
    public bool Passes { get; set; } = true;
    public bool IsReady => Ready;
    public string? SiteKey => Ready ? "site-key" : null;

    public Task<bool> PassesAsync(string? token, string? clientIp, CancellationToken ct = default) =>
        Task.FromResult(Ready && Passes);
}

internal sealed class NoOfficeSchedule : IOfficeScheduleRepository
{
    public Task<OfficeScheduleProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<OfficeScheduleProjection?>(null);

    public void Add(OfficeScheduleProjection projection) { }
}

/// <summary>Horario de 9 a 17 todos los días en Nueva York, con bloqueos puntuales.</summary>
internal sealed class NineToFiveAvailability : IAvailabilityRepository
{
    private readonly List<AvailabilityRule> _rules = [];
    private readonly List<BlockedTime> _blocks = [];

    public void Add(Guid userId) =>
        _rules.Add(
            AvailabilityRule
                .Create(
                    PlatformTenant.Id,
                    userId,
                    DaysOfWeekMask.EveryDay,
                    new TimeOnly(9, 0),
                    new TimeOnly(17, 0),
                    "America/New_York",
                    DateTime.UtcNow
                )
                .Value
        );

    public void Block(Guid userId, DateTime startUtc) =>
        _blocks.Add(
            BlockedTime
                .Create(
                    PlatformTenant.Id,
                    userId,
                    startUtc.AddMinutes(-5),
                    startUtc.AddMinutes(35),
                    "Busy",
                    DateTime.UtcNow
                )
                .Value
        );

    public Task<IReadOnlyList<AvailabilityRule>> ListRulesAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<AvailabilityRule>>([
            .. _rules.Where(r => r.TenantId == tenantId && r.UserId == userId),
        ]);

    public Task<IReadOnlyList<BlockedTime>> ListBlocksAsync(
        Guid tenantId,
        Guid userId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<BlockedTime>>([.. _blocks.Where(b => b.UserId == userId)]);

    public Task<IReadOnlyList<BlockedTime>> ListAllBlocksForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<BlockedTime>>([.. _blocks.Where(b => b.UserId == userId)]);

    public Task<BlockedTime?> FindBlockAsync(Guid tenantId, Guid blockId, CancellationToken ct = default) =>
        Task.FromResult<BlockedTime?>(null);

    public void AddRule(AvailabilityRule rule) => _rules.Add(rule);

    public void AddBlock(BlockedTime block) => _blocks.Add(block);

    public void RemoveBlock(BlockedTime block) => _blocks.Remove(block);
}

internal sealed class PublicBookingStore : IPublicBookingRepository, IBookingPageRepository
{
    public List<BookingPage> Pages { get; } = [];
    public List<PublicBooking> Bookings { get; } = [];

    public Task<PublicBooking?> GetByIdAsync(Guid tenantId, Guid bookingId, CancellationToken ct = default) =>
        Task.FromResult(Bookings.Find(b => b.TenantId == tenantId && b.Id == bookingId));

    public Task<PublicBooking?> FindByRequestHandleAsync(byte[] handleHash, CancellationToken ct = default) =>
        Task.FromResult(Bookings.Find(b => b.RequestHandleHash.SequenceEqual(handleHash)));

    public Task<PublicBooking?> FindByManagementTokenAsync(byte[] tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Bookings.Find(b => b.ManagementTokenHash is { } hash && hash.SequenceEqual(tokenHash)));

    public Task<PublicBooking?> FindByIdempotencyKeyAsync(
        Guid tenantId,
        Guid pageId,
        string key,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Bookings.Find(b => b.TenantId == tenantId && b.BookingPageId == pageId && b.IdempotencyKey == key)
        );

    public Task<PublicBooking?> FindActiveByRequesterAsync(
        Guid tenantId,
        Guid pageId,
        string requesterKey,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Bookings.Find(b =>
                b.TenantId == tenantId
                && b.BookingPageId == pageId
                && b.RequesterKey == requesterKey
                && b.Status is PublicBookingStatus.PendingVerification or PublicBookingStatus.Confirmed
            )
        );

    public Task<int> CountRequestsSinceAsync(
        Guid tenantId,
        string requesterKey,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Bookings.Count(b => b.TenantId == tenantId && b.RequesterKey == requesterKey && b.CreatedAtUtc >= sinceUtc)
        );

    public Task<IReadOnlyList<PublicBooking>> ListHoldsAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<PublicBooking>>([
            .. Bookings.Where(b =>
                b.TenantId == tenantId
                && b.Status == PublicBookingStatus.PendingVerification
                && b.HoldExpiresAtUtc > nowUtc
                && b.SlotStartUtc < toUtc
                && b.SlotStartUtc.AddMinutes(b.DurationMinutes) > fromUtc
            ),
        ]);

    public Task<(IReadOnlyList<PublicBooking> Items, int Total)> ListAsync(
        PublicBookingFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var matching = Bookings.Where(b => b.TenantId == filter.TenantId && b.BookingPageId == filter.PageId).ToList();
        return Task.FromResult<(IReadOnlyList<PublicBooking>, int)>(
            ([.. matching.Skip((page - 1) * pageSize).Take(pageSize)], matching.Count)
        );
    }

    public Task<PublicBookingCounts> CountAsync(PublicBookingFilter filter, CancellationToken ct = default)
    {
        var matching = Bookings.Where(b => b.TenantId == filter.TenantId && b.BookingPageId == filter.PageId).ToList();
        return Task.FromResult(
            new PublicBookingCounts(
                matching.GroupBy(b => b.Status).ToDictionary(g => g.Key, g => g.Count()),
                matching.GroupBy(b => b.HeardAboutCode).ToDictionary(g => g.Key, g => g.Count()),
                matching.GroupBy(b => b.EntrySource).ToDictionary(g => g.Key, g => g.Count()),
                matching.GroupBy(b => b.UtmSource ?? string.Empty).ToDictionary(g => g.Key, g => g.Count())
            )
        );
    }

    public Task<IReadOnlyList<BookingPage>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<BookingPage>>([.. Pages.Where(p => p.TenantId == tenantId)]);

    public Task<IReadOnlyList<PublicBooking>> ListExpiredHoldsAsync(
        Guid tenantId,
        Guid hostUserId,
        DateTime slotStartUtc,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<PublicBooking>>([
            .. Bookings.Where(b =>
                b.TenantId == tenantId
                && b.HostUserId == hostUserId
                && b.SlotStartUtc == slotStartUtc
                && b.Status == PublicBookingStatus.PendingVerification
                && b.HoldExpiresAtUtc <= nowUtc
            ),
        ]);

    public Task<IReadOnlyList<PublicBooking>> ListUpcomingByHostAsync(
        Guid tenantId,
        Guid hostUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<PublicBooking>>([
            .. Bookings.Where(b =>
                b.TenantId == tenantId
                && b.HostUserId == hostUserId
                && b.Status == PublicBookingStatus.Confirmed
                && b.SlotStartUtc > nowUtc
            ),
        ]);

    // Las consultas de los jobs se prueban contra SQL real (PublicBookingJobsIntegrationTests); aquí no participan.
    public Task<IReadOnlyList<PublicBookingRef>> ListStaleHoldsAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<PublicBookingRef>>([]);

    public Task<IReadOnlyList<PublicBookingRef>> ListDueRemindersAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<PublicBookingRef>>([]);

    public Task<IReadOnlyList<PublicBookingRef>> ListPurgeCandidatesAsync(
        DateTime unverifiedBeforeUtc,
        DateTime closedBeforeUtc,
        int take,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<PublicBookingRef>>([]);

    public void Remove(PublicBooking booking) => Bookings.Remove(booking);

    public void Add(PublicBooking booking) => Bookings.Add(booking);

    Task<BookingPage?> IBookingPageRepository.GetByIdAsync(Guid tenantId, Guid bookingPageId, CancellationToken ct) =>
        Task.FromResult(Pages.Find(p => p.TenantId == tenantId && p.Id == bookingPageId));

    public Task<BookingPage?> GetBySlugAsync(Guid tenantId, string slug, CancellationToken ct = default) =>
        Task.FromResult(Pages.Find(p => p.TenantId == tenantId && p.Slug.Value == slug));

    public Task<BookingPage?> FindActiveBySlugAsync(
        IReadOnlyCollection<Guid> tenantIds,
        string slug,
        CancellationToken ct = default
    ) => Task.FromResult(Pages.Find(p => tenantIds.Contains(p.TenantId) && p.IsActive && p.Slug.Value == slug));

    public Task<IReadOnlyList<BookingPage>> ListByActiveHostAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<BookingPage>>([
            .. Pages.Where(p => p.TenantId == tenantId && p.Hosts.Any(h => h.UserId == userId && h.IsActive)),
        ]);

    public void Add(BookingPage page) => Pages.Add(page);
}

/// <summary>Llave del visitante para pruebas: un hex de 64 estable por email (el real es un HMAC con clave).</summary>
internal sealed class TestRequesterKeyHasher : IRequesterKeyHasher
{
    public string KeyOf(WorkEmail email) => KeyFor(email.Value);

    public static string KeyFor(string email) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
}
