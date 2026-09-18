using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Devuelve el catálogo singleton de precios de asiento (o null) para los handlers de seats.</summary>
public sealed class FakeSeatPricingRepository(SeatPricing? pricing) : ISeatPricingRepository
{
    public Task<SeatPricing?> GetAsync(CancellationToken ct = default) => Task.FromResult(pricing);

    public Task<SeatPricing?> GetForUpdateAsync(CancellationToken ct = default) => Task.FromResult(pricing);
}
