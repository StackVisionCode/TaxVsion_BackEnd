using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SeatPricingRepository(SubscriptionDbContext db) : ISeatPricingRepository
{
    public Task<SeatPricing?> GetAsync(CancellationToken ct = default) =>
        db.SeatPricings.AsNoTracking().Include(pricing => pricing.PriceTiers).FirstOrDefaultAsync(ct);

    // Trackeado (sin AsNoTracking) para editar precios y persistir.
    public Task<SeatPricing?> GetForUpdateAsync(CancellationToken ct = default) =>
        db.SeatPricings.Include(pricing => pricing.PriceTiers).FirstOrDefaultAsync(ct);
}
