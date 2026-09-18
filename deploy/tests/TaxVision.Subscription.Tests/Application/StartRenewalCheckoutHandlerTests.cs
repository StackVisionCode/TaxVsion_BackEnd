using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.Commands.StartRenewalCheckout;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Inicio de renovación/reactivación self-service por hosted-checkout (Fase 4): solo desde un estado
/// de lapso, resuelve el monto del ciclo server-side y pide a PaymentApp (M2M) la sesión. Espejo de
/// <c>StartSeatCheckoutHandlerTests</c>.</summary>
public sealed class StartRenewalCheckoutHandlerTests
{
    private static StartRenewalCheckoutCommand Command(Guid tenantId) =>
        new(tenantId, "owner@acme.test", "https://app/ok", "https://app/cancel", "Stripe", "Card", Guid.NewGuid());

    private static FakeRenewalCheckoutPaymentClient OkClient() =>
        new(
            Result.Success(
                new RenewalCheckoutClientResult(
                    Guid.NewGuid(),
                    "https://pay.example/xyz",
                    "cs_1",
                    DateTime.UtcNow.AddHours(24)
                )
            )
        );

    [Fact]
    public async Task Creates_an_intent_and_returns_the_checkout_url_from_a_lapse_state()
    {
        var (subscription, plan) = PastDueSubscription();
        var intents = new FakeRenewalCheckoutIntentRepository();
        var client = OkClient();
        var unitOfWork = new FakeUnitOfWork();

        var result = await StartRenewalCheckoutHandler.Handle(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            intents,
            client,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("https://pay.example/xyz", result.Value.CheckoutUrl);

        var intent = Assert.Single(intents.Added);
        Assert.NotNull(intent.SaaSPaymentId);
        Assert.Equal(4900, client.LastRequest!.AmountCents); // precio base mensual (49 USD)
        Assert.Equal("USD", client.LastRequest.Currency);
        Assert.StartsWith("subscription-renewal-checkout-", client.LastRequest.IdempotencyKey);
        Assert.Equal(2, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task An_active_subscription_cannot_start_a_renewal_checkout()
    {
        var (subscription, plan) = PastDueSubscription();
        subscription.RecoverFromPastDue(Guid.Empty, DateTime.UtcNow); // vuelve a Active

        var result = await StartRenewalCheckoutHandler.Handle(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeRenewalCheckoutIntentRepository(),
            OkClient(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.CannotSelfServiceRenew", result.Error.Code);
    }

    [Fact]
    public async Task Surfaces_a_provider_failure()
    {
        var (subscription, plan) = PastDueSubscription();

        var result = await StartRenewalCheckoutHandler.Handle(
            Command(subscription.TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeRenewalCheckoutIntentRepository(),
            new FakeRenewalCheckoutPaymentClient(
                Result.Failure<RenewalCheckoutClientResult>(
                    new Error("Subscription.RenewalCheckout.ProviderError", "boom")
                )
            ),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.RenewalCheckout.ProviderError", result.Error.Code);
    }

    private static (TenantSubscription Subscription, SubscriptionPlan Plan) PastDueSubscription()
    {
        var now = DateTime.UtcNow;
        var (plan, version) = CreatePublishedPlanWithBasePrice("pro", 49m);
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
        subscription.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, now);
        return (subscription, plan);
    }

    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version) CreatePublishedPlanWithBasePrice(
        string code,
        decimal monthlyUsd
    )
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly])
            .Value;
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(monthlyUsd, "USD").Value).Value
        );
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        Assert.True(plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow).IsSuccess);
        return (plan, version);
    }
}
