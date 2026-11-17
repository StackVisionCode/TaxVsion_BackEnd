using TaxVision.Subscription.Application.Seats.Queries;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>La cotización server-authoritative de compra de asientos: precio unitario del catálogo +
/// prorrateo al período vigente (el mismo cálculo que la compra), sin computar precio en el cliente.</summary>
public sealed class GetSeatQuoteHandlerTests
{
    [Fact]
    public async Task Returns_unit_price_and_prorated_total_for_the_quantity()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();

        var result = await GetSeatQuoteHandler.Handle(
            new GetSeatQuoteQuery(subscription.TenantId, "Standard", Quantity: 3),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            new NoSubscriptionDiscounts(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var quote = result.Value;
        Assert.Equal("Standard", quote.SeatType);
        Assert.Equal(3, quote.Quantity);
        Assert.Equal("Monthly", quote.BillingCycle);
        Assert.Equal("USD", quote.Currency);
        Assert.Equal(1500, quote.UnitAmountCents); // $15 precio de período completo
        Assert.InRange(quote.ProratedUnitAmountCents, 1, 1500); // prorrateado a lo que resta del período
        Assert.Equal(quote.ProratedUnitAmountCents * 3, quote.ProratedTotalCents);
        Assert.Equal(subscription.CurrentPeriodEndUtc, quote.CurrentPeriodEndUtc);
    }

    [Fact]
    public async Task The_quote_shows_the_discount_the_checkout_will_apply()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();

        var result = await GetSeatQuoteHandler.Handle(
            new GetSeatQuoteQuery(subscription.TenantId, "Standard", Quantity: 3),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            new TwentyPercentOnSeats(),
            CancellationToken.None
        );

        var quote = result.Value;
        var expected = quote.ProratedTotalCents / 5;
        Assert.Equal(expected, quote.DiscountCents);
        Assert.Equal(quote.ProratedTotalCents - expected, quote.NetTotalCents);
    }

    [Fact]
    public async Task Without_discount_the_quote_has_no_discount_line()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();

        var result = await GetSeatQuoteHandler.Handle(
            new GetSeatQuoteQuery(subscription.TenantId, "Standard", Quantity: 1),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            new NoSubscriptionDiscounts(),
            CancellationToken.None
        );

        Assert.Null(result.Value.DiscountCents);
        Assert.Equal(result.Value.ProratedTotalCents, result.Value.NetTotalCents);
    }

    private sealed class TwentyPercentOnSeats
        : TaxVision.Subscription.Application.Abstractions.ISubscriptionDiscountReader
    {
        public Task<(long Cents, DiscountSource? Source, string? Label)> ForChargeAsync(
            Guid tenantId,
            DiscountChargeKind kind,
            long grossCents,
            string currency,
            DateTime chargeDateUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult<(long, DiscountSource?, string?)>(
                kind == DiscountChargeKind.Seats
                    ? (grossCents / 5, DiscountSource.Code, "Promo code · 20% off")
                    : (0, null, null)
            );
    }

    [Fact]
    public async Task Fails_for_an_unknown_seat_type()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();

        var result = await GetSeatQuoteHandler.Handle(
            new GetSeatQuoteQuery(subscription.TenantId, "Bogus", Quantity: 1),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            new NoSubscriptionDiscounts(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.InvalidType", result.Error.Code);
    }

    [Fact]
    public async Task Fails_when_the_tenant_has_no_subscription()
    {
        var (_, pricing) = ActiveSubscriptionWithPricing();

        var result = await GetSeatQuoteHandler.Handle(
            new GetSeatQuoteQuery(Guid.NewGuid(), "Standard", Quantity: 1),
            new FakeSubscriptionRepo(subscription: null),
            new FakeSeatPricingRepository(pricing),
            new NoSubscriptionDiscounts(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.NotFound", result.Error.Code);
    }

    private static (TenantSubscription Subscription, SeatPricing Pricing) ActiveSubscriptionWithPricing()
    {
        var now = DateTime.UtcNow;
        var (plan, version) = CreatePublishedPlan("pro");
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                now,
                now.AddDays(30),
                Guid.Empty,
                now
            )
            .Value;

        var pricing = SeatPricing.Seed(Guid.NewGuid(), now);
        pricing.AddPriceTier(
            SeatPriceTier
                .Create(pricing.Id, SeatType.Standard, BillingCycle.Monthly, Money.Create(15m, "USD").Value)
                .Value
        );
        pricing.AddPriceTier(
            SeatPriceTier
                .Create(pricing.Id, SeatType.Standard, BillingCycle.Yearly, Money.Create(150m, "USD").Value)
                .Value
        );
        return (subscription, pricing);
    }

    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version) CreatePublishedPlan(string code)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Starter, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        PlanTestData.EnsurePublishableFeature(version);
        PlanTestData.EnsurePublishablePrice(version);
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        Assert.True(plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow).IsSuccess);
        return (plan, version);
    }
}
