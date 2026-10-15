using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Billing.Commands.SetBillingPreference;
using TaxVision.Subscription.Domain.Billing;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>SetBillingPreference: alta/switch del aggregate y publicación del
/// evento proyectado en PaymentApp. El monto sale del plan vigente server-side,
/// nunca del request.</summary>
public sealed class SetBillingPreferenceHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task Choose_automatic_persists_mandate_and_publishes_event()
    {
        var (subscription, plan) = ActiveSubscriptionWithMonthlyPrice(_tenantId, 29m);
        var repo = new FakeRepo(existing: null);
        var bus = new CapturingMessageBus();

        var result = await SetBillingPreferenceHandler.Handle(
            new SetBillingPreferenceCommand(
                TenantId: _tenantId,
                ActorUserId: _userId,
                Mode: "Automatic",
                ProviderCode: "Stripe",
                ConsentText: "I agree to automatic billing.",
                ConsentTextVersion: "v1-2026-10",
                ConsentIpAddress: "1.2.3.4",
                ConsentUserAgent: "jest"
            ),
            repo,
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger<TenantBillingPreference>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotNull(repo.Added);
        Assert.Equal(BillingMode.Automatic, repo.Added!.Mode);
        var evt = Assert.Single(bus.Published.OfType<TenantBillingPreferenceChangedIntegrationEvent>());
        Assert.Equal("Automatic", evt.Mode);
        Assert.True(evt.HasActiveMandate);
        Assert.Equal(2900, evt.AgreedAmountCents);
    }

    [Fact]
    public async Task Automatic_requires_consent_text_and_ip()
    {
        // Validaciones de consent corren antes de resolver el plan → fakes vacíos.
        var result = await SetBillingPreferenceHandler.Handle(
            new SetBillingPreferenceCommand(
                _tenantId,
                _userId,
                "Automatic",
                "Stripe",
                ConsentText: null,
                ConsentTextVersion: null,
                ConsentIpAddress: "1.2.3.4",
                ConsentUserAgent: null
            ),
            new FakeRepo(null),
            new FakeSubscriptionRepo(subscription: null),
            new FakePlanRepo(plan: null),
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<TenantBillingPreference>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("BillingPreference.MissingConsentText", result.Error.Code);
    }

    [Fact]
    public async Task Automatic_fails_when_plan_has_no_positive_price()
    {
        // Plan publicado pero sin priceTier: ResolveBaseSubscriptionPrice devuelve null
        // y el handler corta con NoPositivePrice antes de tocar el aggregate.
        var (subscription, plan) = ActiveSubscriptionWithoutPriceTier(_tenantId);
        var repo = new FakeRepo(existing: null);
        var bus = new CapturingMessageBus();

        var result = await SetBillingPreferenceHandler.Handle(
            new SetBillingPreferenceCommand(
                _tenantId,
                _userId,
                Mode: "Automatic",
                ProviderCode: "Stripe",
                ConsentText: "I agree to automatic billing.",
                ConsentTextVersion: "v1-2026-10",
                ConsentIpAddress: "1.2.3.4",
                ConsentUserAgent: "jest"
            ),
            repo,
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger<TenantBillingPreference>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("BillingPreference.NoPositivePrice", result.Error.Code);
        Assert.Null(repo.Added);
        Assert.Empty(bus.Published);
    }

    private static (TenantSubscription Subscription, SubscriptionPlan Plan) ActiveSubscriptionWithMonthlyPrice(
        Guid tenantId,
        decimal monthlyUsd
    )
    {
        var (plan, version) = CreatePublishedPlan("pro", monthlyUsd);
        var now = DateTime.UtcNow;
        var subscription = TenantSubscription
            .ActivateImmediately(tenantId, plan, version, BillingCycle.Monthly, now, now.AddDays(30), Guid.Empty, now)
            .Value;
        return (subscription, plan);
    }

    private static (TenantSubscription Subscription, SubscriptionPlan Plan) ActiveSubscriptionWithoutPriceTier(
        Guid tenantId
    )
    {
        var (plan, version) = CreatePublishedPlan("pro", monthlyUsd: null);
        var now = DateTime.UtcNow;
        var subscription = TenantSubscription
            .ActivateImmediately(tenantId, plan, version, BillingCycle.Monthly, now, now.AddDays(30), Guid.Empty, now)
            .Value;
        return (subscription, plan);
    }

    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version) CreatePublishedPlan(
        string code,
        decimal? monthlyUsd
    )
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly])
            .Value;
        if (monthlyUsd is { } amount)
        {
            version.AddPriceTier(
                PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(amount, "USD").Value).Value
            );
        }
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        Assert.True(plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow).IsSuccess);
        return (plan, version);
    }

    private sealed class FakeRepo(TenantBillingPreference? existing) : ITenantBillingPreferenceRepository
    {
        public TenantBillingPreference? Added { get; private set; }

        public Task<TenantBillingPreference?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(existing);

        public Task AddAsync(TenantBillingPreference preference, CancellationToken ct = default)
        {
            Added = preference;
            return Task.CompletedTask;
        }
    }
}
