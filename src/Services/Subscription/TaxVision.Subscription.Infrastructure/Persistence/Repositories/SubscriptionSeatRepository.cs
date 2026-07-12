using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionSeatRepository(SubscriptionDbContext db) : ISubscriptionSeatRepository
{
    public Task<SubscriptionSeat?> GetByIdAsync(Guid seatId, Guid tenantId, CancellationToken ct = default) =>
        db.Seats.FirstOrDefaultAsync(seat => seat.Id == seatId && seat.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<SubscriptionSeat>> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.Seats.AsNoTracking().Where(seat => seat.TenantId == tenantId).ToListAsync(ct);

    public async Task AddAsync(SubscriptionSeat seat, CancellationToken ct = default) =>
        await db.Seats.AddAsync(seat, ct);
}
