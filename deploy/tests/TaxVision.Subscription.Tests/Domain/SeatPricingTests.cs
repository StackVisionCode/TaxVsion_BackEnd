using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

/// <summary>
/// El catálogo GLOBAL singleton de precios de asiento: resuelve el precio por (tipo, ciclo) para la compra,
/// y el PlatformAdmin lo edita por tipo (upsert) sin tocar los demás tipos.
/// </summary>
public sealed class SeatPricingTests
{
    private static SeatPricing NewCatalog() => SeatPricing.Seed(Guid.NewGuid(), DateTime.UtcNow);

    private static void AddTier(SeatPricing pricing, SeatType type, BillingCycle cycle, decimal usd) =>
        pricing.AddPriceTier(SeatPriceTier.Create(pricing.Id, type, cycle, Money.Create(usd, "USD").Value).Value);

    [Fact]
    public void ResolveUnitPrice_returns_the_tier_for_type_and_cycle()
    {
        var pricing = NewCatalog();
        AddTier(pricing, SeatType.Standard, BillingCycle.Monthly, 12m);
        AddTier(pricing, SeatType.Standard, BillingCycle.Yearly, 120m);

        var monthly = pricing.ResolveUnitPrice(SeatType.Standard, BillingCycle.Monthly);
        var yearly = pricing.ResolveUnitPrice(SeatType.Standard, BillingCycle.Yearly);

        Assert.True(monthly.IsSuccess);
        Assert.Equal(12m, monthly.Value.Amount);
        Assert.Equal(120m, yearly.Value.Amount);
    }

    [Fact]
    public void ResolveUnitPrice_fails_when_no_tier_matches()
    {
        var pricing = NewCatalog();

        var result = pricing.ResolveUnitPrice(SeatType.Standard, BillingCycle.Monthly);

        Assert.True(result.IsFailure);
        Assert.Equal("SeatPricing.NoPriceTier", result.Error.Code);
    }

    [Fact]
    public void SetTypePrices_updates_the_tier_in_place_without_touching_other_types()
    {
        var pricing = NewCatalog();
        AddTier(pricing, SeatType.Standard, BillingCycle.Monthly, 12m);
        AddTier(pricing, SeatType.Standard, BillingCycle.Yearly, 120m);
        AddTier(pricing, SeatType.Portal, BillingCycle.Monthly, 5m);
        AddTier(pricing, SeatType.Portal, BillingCycle.Yearly, 50m);

        var result = pricing.SetTypePrices(
            SeatType.Standard,
            Money.Create(20m, "USD").Value,
            Money.Create(200m, "USD").Value,
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(4, pricing.PriceTiers.Count); // upsert, no duplicados
        Assert.Equal(20m, pricing.ResolveUnitPrice(SeatType.Standard, BillingCycle.Monthly).Value.Amount);
        Assert.Equal(200m, pricing.ResolveUnitPrice(SeatType.Standard, BillingCycle.Yearly).Value.Amount);
        Assert.Equal(5m, pricing.ResolveUnitPrice(SeatType.Portal, BillingCycle.Monthly).Value.Amount);
    }

    [Fact]
    public void SetTypePrices_adds_the_tiers_when_the_type_has_none()
    {
        var pricing = NewCatalog();

        pricing.SetTypePrices(
            SeatType.Signature,
            Money.Create(8m, "USD").Value,
            Money.Create(80m, "USD").Value,
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.Equal(8m, pricing.ResolveUnitPrice(SeatType.Signature, BillingCycle.Monthly).Value.Amount);
        Assert.Equal(80m, pricing.ResolveUnitPrice(SeatType.Signature, BillingCycle.Yearly).Value.Amount);
    }
}
