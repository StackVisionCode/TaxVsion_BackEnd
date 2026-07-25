using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionSeatRepository(SubscriptionDbContext db) : ISubscriptionSeatRepository
{
    public Task<SubscriptionSeat?> GetByIdAsync(Guid seatId, Guid tenantId, CancellationToken ct = default) =>
        WithChildren(db.Seats)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(seat => seat.Id == seatId && seat.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<SubscriptionSeat>> GetByTenantIdAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) =>
        await WithChildren(db.Seats.AsNoTracking())
            .IgnoreQueryFilters()
            .Where(seat => seat.TenantId == tenantId)
            .ToListAsync(ct);

    public Task<SubscriptionSeat?> GetByCurrentUserIdAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        WithChildren(db.Seats.AsNoTracking())
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(seat => seat.TenantId == tenantId && seat.CurrentUserId == userId, ct);

    public async Task AddAsync(SubscriptionSeat seat, CancellationToken ct = default) =>
        await db.Seats.AddAsync(seat, ct);

    // IgnoreQueryFilters: jobs cross-tenant (RBAC Fase 5) — recorren seats de todos los tenants
    // buscando renovaciones/expiraciones vencidas, nunca sirven una request autenticada.
    public async Task<IReadOnlyList<SubscriptionSeat>> GetDueForRenewalAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await WithChildren(db.Seats)
            .IgnoreQueryFilters()
            .Where(seat =>
                seat.Status == SeatStatus.Active
                && seat.AutoRenew
                && seat.NextRenewalAtUtc != null
                && seat.NextRenewalAtUtc <= nowUtc
            )
            .OrderBy(seat => seat.NextRenewalAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SubscriptionSeat>> GetPastGracePeriodAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .Seats.IgnoreQueryFilters()
            .Where(seat =>
                seat.Status == SeatStatus.GracePeriod
                && seat.GracePeriodEndsAtUtc != null
                && seat.GracePeriodEndsAtUtc <= nowUtc
            )
            .OrderBy(seat => seat.GracePeriodEndsAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SubscriptionSeat>> GetSuspendedBeforeAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .Seats.IgnoreQueryFilters()
            .Where(seat =>
                seat.Status == SeatStatus.Suspended && seat.SuspendedAtUtc != null && seat.SuspendedAtUtc <= cutoffUtc
            )
            .OrderBy(seat => seat.SuspendedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SubscriptionSeat>> GetCancelledPastPeriodEndAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .Seats.IgnoreQueryFilters()
            .Where(seat =>
                seat.Status == SeatStatus.Cancelled
                && seat.CurrentPeriodEndUtc != null
                && seat.CurrentPeriodEndUtc <= nowUtc
            )
            .OrderBy(seat => seat.CurrentPeriodEndUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SubscriptionSeat>> GetRenewingBetweenAsync(
        DateTime fromUtc,
        DateTime toUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await db
            .Seats.IgnoreQueryFilters()
            .Where(seat =>
                seat.Status == SeatStatus.Active
                && seat.AutoRenew
                && seat.NextRenewalAtUtc != null
                && seat.NextRenewalAtUtc >= fromUtc
                && seat.NextRenewalAtUtc <= toUtc
            )
            .OrderBy(seat => seat.NextRenewalAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    // IgnoreQueryFilters: query admin cross-tenant (GetExpiredSeatsHandler, PlatformAdmin only) —
    // lista seats expirados de todos los tenants a propósito.
    public async Task<(IReadOnlyList<SubscriptionSeat> Items, int TotalCount)> GetExpiredAsync(
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = db.Seats.IgnoreQueryFilters().AsNoTracking().Where(seat => seat.Status == SeatStatus.Expired);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(seat => seat.ExpiredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    private static IQueryable<SubscriptionSeat> WithChildren(IQueryable<SubscriptionSeat> query) =>
        query.Include(seat => seat.Assignments).Include(seat => seat.Renewals);
}
