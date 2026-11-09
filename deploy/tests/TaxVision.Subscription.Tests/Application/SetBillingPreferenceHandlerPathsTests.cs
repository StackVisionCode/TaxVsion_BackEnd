using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Billing.Commands.SetBillingPreference;
using TaxVision.Subscription.Application.Billing.Queries;
using TaxVision.Subscription.Domain.Billing;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>SUB-11: caracteriza rechazos, avisos y respuesta de la preferencia de cobro antes de adelgazar el handler.</summary>
public sealed class SetBillingPreferenceHandlerPathsTests
{
    [Fact]
    public async Task An_unknown_mode_is_refused()
    {
        var world = new World();

        var result = await world.SetAsync(mode: "Sometimes");

        world.AssertRefused(result, "BillingPreference.InvalidMode", "'Sometimes' is not a valid mode.");
    }

    [Fact]
    public async Task Automatic_needs_a_provider()
    {
        var world = new World();

        var result = await world.SetAsync(provider: " ");

        world.AssertRefused(
            result,
            "BillingPreference.InvalidProvider",
            "ProviderCode is required for Automatic mode."
        );
    }

    [Fact]
    public async Task Automatic_needs_a_subscription_to_anchor_the_mandate()
    {
        var world = new World { Subscriptions = new FakeSubscriptionRepo(null) };

        var result = await world.SetAsync();

        world.AssertRefused(
            result,
            "BillingPreference.NoSubscription",
            "Tenant has no subscription to anchor the mandate to."
        );
    }

    [Fact]
    public async Task Automatic_needs_the_plan()
    {
        var world = new World { Plans = new FakePlanRepo(null) };

        var result = await world.SetAsync();

        world.AssertRefused(result, "BillingPreference.PlanNotFound", "Plan does not exist.");
    }

    [Fact]
    public async Task The_first_automatic_choice_announces_preference_mode_and_mandate_in_that_order()
    {
        var world = new World();

        var result = await world.SetAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("Automatic", result.Value.Mode);
        Assert.Equal("Stripe", result.Value.ProviderCode);
        Assert.True(result.Value.HasActiveMandate);
        Assert.Equal(2900, result.Value.AgreedAmountCents);
        Assert.Equal("USD", result.Value.AgreedCurrency);
        Assert.Equal("monthly", result.Value.AgreedCadence);
        Assert.Equal(1, world.UnitOfWork.SaveChangesCallCount);

        var changed = Assert.IsType<TenantBillingPreferenceChangedIntegrationEvent>(world.Bus.Published[0]);
        Assert.Equal(world.Tenant, changed.TenantId);
        Assert.Equal("Stripe", changed.ProviderCode);
        Assert.Equal(2900, changed.AgreedAmountCents);
        Assert.Equal(world.Repository.Current!.UpdatedAtUtc.Ticks, changed.RevisionTicks);

        var mode = Assert.IsType<TenantSubscriptionStatusChangedIntegrationEvent>(world.Bus.Published[1]);
        Assert.Equal(nameof(SubscriptionChangeReason.BillingModeChanged), mode.Reason);
        Assert.Equal("Automatic", mode.NewMode);
        Assert.Equal("29.00", mode.Amount);
        Assert.Equal("monthly", mode.Cadence);
        Assert.Equal(Guid.Empty, mode.TenantSubscriptionId);
        Assert.Equal("Active", mode.Status);

        var mandate = Assert.IsType<TenantSubscriptionStatusChangedIntegrationEvent>(world.Bus.Published[2]);
        Assert.Equal(nameof(SubscriptionChangeReason.MandateCreated), mandate.Reason);
        Assert.Equal("29.00", mandate.Amount);
        Assert.Equal(3, world.Bus.Published.Count);
    }

    [Fact]
    public async Task Manual_needs_no_plan_and_announces_preference_and_mode()
    {
        var world = new World { Subscriptions = new FakeSubscriptionRepo(null), Plans = new FakePlanRepo(null) };

        var result = await world.SetAsync(mode: "manual", provider: null, consentText: null, consentIp: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Manual", result.Value.Mode);
        Assert.False(result.Value.HasActiveMandate);
        Assert.Null(result.Value.AgreedAmountCents);
        Assert.Equal(2, world.Bus.Published.Count);
        var mode = Assert.IsType<TenantSubscriptionStatusChangedIntegrationEvent>(world.Bus.Published[1]);
        Assert.Equal("Manual", mode.NewMode);
        Assert.Null(mode.Amount);
    }

    [Fact]
    public async Task Switching_back_to_manual_keeps_the_same_row_and_drops_the_mandate()
    {
        var world = new World();
        await world.SetAsync();
        var preference = world.Repository.Current!;
        world.Bus.Published.Clear();

        var result = await world.SetAsync(mode: "Manual", provider: null, consentText: null, consentIp: null);

        Assert.True(result.IsSuccess);
        Assert.Same(preference, world.Repository.Current);
        Assert.Equal(BillingMode.Manual, preference.Mode);
        Assert.False(result.Value.HasActiveMandate);
        Assert.Equal(2, world.Bus.Published.Count);
    }

    [Fact]
    public async Task Repeating_automatic_announces_only_the_preference()
    {
        var world = new World();
        await world.SetAsync();
        world.Bus.Published.Clear();

        var result = await world.SetAsync();

        Assert.True(result.IsSuccess);
        Assert.IsType<TenantBillingPreferenceChangedIntegrationEvent>(Assert.Single(world.Bus.Published));
    }

    private sealed class World
    {
        public World()
        {
            var now = DateTime.UtcNow;
            var plan = SubscriptionPlan
                .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Starter, Guid.Empty, now)
                .Value;
            var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
            version.AddPriceTier(
                PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(29m, "USD").Value).Value
            );
            PlanTestData.EnsurePublishableFeature(version);
            plan.AddVersion(version, Guid.Empty, now);
            Assert.True(plan.PublishVersion(version.Id, now, Guid.Empty, now).IsSuccess);
            var subscription = TenantSubscription
                .ActivateImmediately(Tenant, plan, version, BillingCycle.Monthly, now, now.AddDays(30), Guid.Empty, now)
                .Value;
            Subscriptions = new FakeSubscriptionRepo(subscription);
            Plans = new FakePlanRepo(plan);
        }

        public Guid Tenant { get; } = Guid.NewGuid();
        public FakeSubscriptionRepo Subscriptions { get; init; }
        public FakePlanRepo Plans { get; init; }
        public OnePreference Repository { get; } = new();
        public CapturingMessageBus Bus { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();

        public void AssertRefused(Result<BillingPreferenceResponse> result, string code, string message)
        {
            Assert.Equal(code, result.Error.Code);
            Assert.Equal(message, result.Error.Message);
            Assert.Null(Repository.Current);
            Assert.Empty(Bus.Published);
            Assert.Equal(0, UnitOfWork.SaveChangesCallCount);
        }

        public Task<Result<BillingPreferenceResponse>> SetAsync(
            string mode = "Automatic",
            string? provider = "Stripe",
            string? consentText = "I agree to automatic billing.",
            string? consentIp = "1.2.3.4"
        ) =>
            SetBillingPreferenceHandler.Handle(
                new SetBillingPreferenceCommand(
                    Tenant,
                    Guid.NewGuid(),
                    mode,
                    provider,
                    consentText,
                    "v1",
                    consentIp,
                    "xunit"
                ),
                Repository,
                Subscriptions,
                Plans,
                UnitOfWork,
                Bus,
                new FakeCorrelationContext(),
                NullLogger<TenantBillingPreference>.Instance,
                CancellationToken.None
            );
    }

    private sealed class OnePreference : ITenantBillingPreferenceRepository
    {
        public TenantBillingPreference? Current { get; private set; }

        public Task<TenantBillingPreference?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Current);

        public Task AddAsync(TenantBillingPreference preference, CancellationToken ct = default)
        {
            Current = preference;
            return Task.CompletedTask;
        }
    }
}
