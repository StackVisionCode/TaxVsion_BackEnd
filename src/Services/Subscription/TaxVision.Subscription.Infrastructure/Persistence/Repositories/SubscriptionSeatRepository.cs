using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionSeatRepository(SubscriptionDbContext db) : ISubscriptionSeatRepository
{
    public Task<SubscriptionSeat?> GetByIdAsync(Guid seatId, Guid tenantId, CancellationToken ct = default) =>
        WithAssignments(db.Seats).FirstOrDefaultAsync(seat => seat.Id == seatId && seat.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<SubscriptionSeat>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        await WithAssignments(db.Seats.AsNoTracking()).Where(seat => seat.TenantId == tenantId).ToListAsync(ct);

    public Task<SubscriptionSeat?> GetByCurrentUserIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        WithAssignments(db.Seats.AsNoTracking()).FirstOrDefaultAsync(seat => seat.TenantId == tenantId && seat.CurrentUserId == userId, ct);

    public async Task AddAsync(SubscriptionSeat seat, CancellationToken ct = default) =>
        await db.Seats.AddAsync(seat, ct);

    private static IQueryable<SubscriptionSeat> WithAssignments(IQueryable<SubscriptionSeat> query) =>
        query.Include(seat => seat.Assignments);
}
