using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

/// <summary>Las reservas públicas se leen sin tenant en contexto (visitante anónimo): el tenant va explícito.</summary>
public sealed class PublicBookingRepository(CalendarDbContext context) : IPublicBookingRepository
{
    public Task<PublicBooking?> GetByIdAsync(Guid tenantId, Guid bookingId, CancellationToken ct = default) =>
        Bookings().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == bookingId, ct);

    // Los dos secretos tienen índice único: el hash apunta a una sola reserva de cualquier tenant.
    public Task<PublicBooking?> FindByRequestHandleAsync(byte[] handleHash, CancellationToken ct = default) =>
        Bookings().FirstOrDefaultAsync(b => b.RequestHandleHash == handleHash, ct);

    public Task<PublicBooking?> FindByManagementTokenAsync(byte[] tokenHash, CancellationToken ct = default) =>
        Bookings().FirstOrDefaultAsync(b => b.ManagementTokenHash == tokenHash, ct);

    public Task<PublicBooking?> FindByIdempotencyKeyAsync(
        Guid tenantId,
        Guid pageId,
        string key,
        CancellationToken ct = default
    ) =>
        Bookings()
            .FirstOrDefaultAsync(
                b => b.TenantId == tenantId && b.BookingPageId == pageId && b.IdempotencyKey == key,
                ct
            );

    public Task<PublicBooking?> FindActiveByRequesterAsync(
        Guid tenantId,
        Guid pageId,
        string requesterKey,
        CancellationToken ct = default
    ) =>
        Bookings()
            .FirstOrDefaultAsync(
                b =>
                    b.TenantId == tenantId
                    && b.BookingPageId == pageId
                    && b.RequesterKey == requesterKey
                    && (
                        b.Status == PublicBookingStatus.PendingVerification || b.Status == PublicBookingStatus.Confirmed
                    ),
                ct
            );

    public Task<int> CountRequestsSinceAsync(
        Guid tenantId,
        string requesterKey,
        DateTime sinceUtc,
        CancellationToken ct = default
    ) =>
        Bookings()
            .CountAsync(
                b => b.TenantId == tenantId && b.RequesterKey == requesterKey && b.CreatedAtUtc >= sinceUtc,
                ct
            );

    // Sin seguimiento: solo se leen para restar horarios; la que se toca se carga aparte.
    public async Task<IReadOnlyList<PublicBooking>> ListHoldsAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        await Bookings()
            .AsNoTracking()
            .Where(b =>
                b.TenantId == tenantId
                && b.Status == PublicBookingStatus.PendingVerification
                && b.HoldExpiresAtUtc > nowUtc
                && b.SlotStartUtc < toUtc
                && b.SlotStartUtc >= fromUtc
            )
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<PublicBooking> Items, int Total)> ListAsync(
        PublicBookingFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = Filtered(filter);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(b => b.SlotStartUtc)
            .ThenBy(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    // Agrupado en SQL: el panel nunca trae las filas para contarlas.
    public async Task<PublicBookingCounts> CountAsync(PublicBookingFilter filter, CancellationToken ct = default)
    {
        var query = Filtered(filter);
        var byStatus = await query.GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var byHeardAbout = await query
            .GroupBy(b => b.HeardAboutCode)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var bySource = await query
            .GroupBy(b => b.EntrySource)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var byUtm = await query
            .GroupBy(b => b.UtmSource ?? string.Empty)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new PublicBookingCounts(
            byStatus.ToDictionary(row => row.Key, row => row.Count),
            byHeardAbout.ToDictionary(row => row.Key, row => row.Count),
            bySource.ToDictionary(row => row.Key, row => row.Count),
            byUtm.ToDictionary(row => row.Key, row => row.Count)
        );
    }

    public async Task<IReadOnlyList<PublicBooking>> ListExpiredHoldsAsync(
        Guid tenantId,
        Guid hostUserId,
        DateTime slotStartUtc,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        await Bookings()
            .Where(b =>
                b.TenantId == tenantId
                && b.HostUserId == hostUserId
                && b.SlotStartUtc == slotStartUtc
                && b.Status == PublicBookingStatus.PendingVerification
                && b.HoldExpiresAtUtc <= nowUtc
            )
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PublicBooking>> ListUpcomingByHostAsync(
        Guid tenantId,
        Guid hostUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        await Bookings()
            .Where(b =>
                b.TenantId == tenantId
                && b.HostUserId == hostUserId
                && b.Status == PublicBookingStatus.Confirmed
                && b.SlotStartUtc > nowUtc
            )
            .OrderBy(b => b.SlotStartUtc)
            .ToListAsync(ct);

    // Los jobs barren todos los tenants: tenant explícito en cada referencia, sin filtro de contexto.
    public async Task<IReadOnlyList<PublicBookingRef>> ListStaleHoldsAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct = default
    ) =>
        await Bookings()
            .AsNoTracking()
            .Where(b => b.Status == PublicBookingStatus.PendingVerification && b.HoldExpiresAtUtc <= nowUtc)
            .OrderBy(b => b.HoldExpiresAtUtc)
            .Take(take)
            .Select(b => new PublicBookingRef(b.TenantId, b.Id))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PublicBookingRef>> ListDueRemindersAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct = default
    ) =>
        await (
            from booking in Bookings().AsNoTracking()
            join page in context.BookingPages.IgnoreQueryFilters().AsNoTracking()
                on new { booking.TenantId, Id = booking.BookingPageId } equals new { page.TenantId, page.Id }
            where
                booking.Status == PublicBookingStatus.Confirmed
                && booking.ReminderSentAtUtc == null
                && booking.SlotStartUtc > nowUtc
                && page.ReminderHoursBefore != null
                && booking.SlotStartUtc <= nowUtc.AddHours((double)page.ReminderHoursBefore)
                && booking.UpdatedAtUtc < booking.SlotStartUtc.AddHours(-(double)page.ReminderHoursBefore)
            orderby booking.SlotStartUtc
            select new PublicBookingRef(booking.TenantId, booking.Id)
        )
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PublicBookingRef>> ListPurgeCandidatesAsync(
        DateTime unverifiedBeforeUtc,
        DateTime closedBeforeUtc,
        int take,
        CancellationToken ct = default
    ) =>
        await Bookings()
            .AsNoTracking()
            .Where(b =>
                (b.Status == PublicBookingStatus.Expired && b.UpdatedAtUtc <= unverifiedBeforeUtc)
                || (
                    (
                        b.Status == PublicBookingStatus.Cancelled
                        || b.Status == PublicBookingStatus.Completed
                        || b.Status == PublicBookingStatus.NoShow
                    )
                    && b.AnonymizedAtUtc == null
                    && b.UpdatedAtUtc <= closedBeforeUtc
                )
            )
            .OrderBy(b => b.UpdatedAtUtc)
            .Take(take)
            .Select(b => new PublicBookingRef(b.TenantId, b.Id))
            .ToListAsync(ct);

    public void Add(PublicBooking booking) => context.PublicBookings.Add(booking);

    public void Remove(PublicBooking booking) => context.PublicBookings.Remove(booking);

    private IQueryable<PublicBooking> Filtered(PublicBookingFilter filter)
    {
        var query = Bookings()
            .AsNoTracking()
            .Where(b =>
                b.TenantId == filter.TenantId
                && b.BookingPageId == filter.PageId
                && b.SlotStartUtc >= filter.FromUtc
                && b.SlotStartUtc < filter.ToUtc
            );
        if (filter.Status is { } status)
            query = query.Where(b => b.Status == status);
        if (filter.HeardAbout is { } code)
            query = query.Where(b => b.HeardAboutCode == code);
        if (filter.Source is { } source)
            query = query.Where(b => b.EntrySource == source);
        return query;
    }

    private IQueryable<PublicBooking> Bookings() => context.PublicBookings.IgnoreQueryFilters();
}
