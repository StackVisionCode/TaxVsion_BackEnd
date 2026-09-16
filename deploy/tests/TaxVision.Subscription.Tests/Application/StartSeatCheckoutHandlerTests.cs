using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Seats.Commands.StartSeatCheckout;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>El inicio de compra por hosted-checkout (tenant sin método): crea la intención, resuelve el total
/// server-side y pide a PaymentApp (M2M) la sesión, devolviendo la URL. Un tipo gratis no puede ir por acá.</summary>
public sealed class StartSeatCheckoutHandlerTests
{
    private static StartSeatCheckoutCommand Command(Guid tenantId, string seatType, int quantity) =>
        new(
            tenantId,
            seatType,
            quantity,
            AutoRenew: true,
            "owner@acme.test",
            "https://app/ok",
            "https://app/cancel",
            "Stripe",
            "Card",
            Guid.NewGuid()
        );

    [Fact]
    public async Task Creates_an_intent_and_returns_the_checkout_url()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();
        var intents = new FakeSeatPurchaseIntentRepository();
        var client = new FakeSeatCheckoutPaymentClient(
            Result.Success(
                new SeatCheckoutClientResult(
                    Guid.NewGuid(),
                    "https://pay.example/xyz",
                    "cs_123",
                    DateTime.UtcNow.AddHours(24)
                )
            )
        );
        var unitOfWork = new FakeUnitOfWork();

        var result = await StartSeatCheckoutHandler.Handle(
            Command(subscription.TenantId, "Standard", 2),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            intents,
            client,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("https://pay.example/xyz", result.Value.CheckoutUrl);

        var intent = Assert.Single(intents.Added);
        Assert.NotNull(intent.SaaSPaymentId);
        Assert.Equal("https://pay.example/xyz", intent.CheckoutUrl);
        Assert.NotNull(client.LastRequest);
        Assert.Equal(intent.ProratedTotalCents, client.LastRequest!.AmountCents);
        Assert.Equal("USD", client.LastRequest.Currency);
        Assert.StartsWith("seat-checkout-", client.LastRequest.IdempotencyKey);
        Assert.Equal(2, unitOfWork.SaveChangesCallCount); // guarda la intención, y de nuevo tras adjuntar el checkout
    }

    [Fact]
    public async Task A_free_seat_type_cannot_use_checkout()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();

        var result = await StartSeatCheckoutHandler.Handle(
            Command(subscription.TenantId, "Portal", 1),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            new FakeSeatPurchaseIntentRepository(),
            new FakeSeatCheckoutPaymentClient(
                Result.Success(new SeatCheckoutClientResult(Guid.NewGuid(), "x", "y", DateTime.UtcNow))
            ),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Seats.Checkout.NothingToCharge", result.Error.Code);
    }

    [Fact]
    public async Task Surfaces_a_provider_failure()
    {
        var (subscription, pricing) = ActiveSubscriptionWithPricing();

        var result = await StartSeatCheckoutHandler.Handle(
            Command(subscription.TenantId, "Standard", 1),
            new FakeSubscriptionRepo(subscription),
            new FakeSeatPricingRepository(pricing),
            new FakeSeatPurchaseIntentRepository(),
            new FakeSeatCheckoutPaymentClient(
                Result.Failure<SeatCheckoutClientResult>(new Error("Seats.Checkout.ProviderError", "boom"))
            ),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Seats.Checkout.ProviderError", result.Error.Code);
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
        AddTier(pricing, SeatType.Standard, BillingCycle.Monthly, 15m);
        AddTier(pricing, SeatType.Standard, BillingCycle.Yearly, 150m);
        AddTier(pricing, SeatType.Portal, BillingCycle.Monthly, 0m);
        AddTier(pricing, SeatType.Portal, BillingCycle.Yearly, 0m);
        return (subscription, pricing);
    }

    private static void AddTier(SeatPricing pricing, SeatType type, BillingCycle cycle, decimal usd) =>
        pricing.AddPriceTier(SeatPriceTier.Create(pricing.Id, type, cycle, Money.Create(usd, "USD").Value).Value);

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
