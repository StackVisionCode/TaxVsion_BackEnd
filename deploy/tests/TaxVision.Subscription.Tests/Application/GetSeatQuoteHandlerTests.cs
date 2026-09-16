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
    public async Task Fails_for_an_unknown_seat_type()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();

        var result = await GetSeatQuoteHandler.Handle(
            new GetSeatQuoteQuery(subscription.TenantId, "Bogus", Quantity: 1),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
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
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        Assert.True(plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow).IsSuccess);
        return (plan, version);
    }
}
