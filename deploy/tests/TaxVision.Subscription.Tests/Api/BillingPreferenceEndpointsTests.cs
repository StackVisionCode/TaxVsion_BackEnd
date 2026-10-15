using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Security;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Api.Controllers;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Billing.Commands.SetBillingPreference;
using TaxVision.Subscription.Domain.Billing;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Api;

/// <summary>Contrato del endpoint <c>me/billing-preference</c>: a nivel attributes
/// (actor/permiso/superficie/step-up) y a nivel handler (validaciones Automatic vs Manual +
/// publicación del integration event que proyecta en PaymentApp).</summary>
public sealed class BillingPreferenceEndpointsTests
{
    private static readonly MethodInfo GetAction = typeof(SubscriptionsController).GetMethod(
        nameof(SubscriptionsController.GetBillingPreference)
    )!;

    private static readonly MethodInfo SetAction = typeof(SubscriptionsController).GetMethod(
        nameof(SubscriptionsController.SetBillingPreference)
    )!;

    // ---------- Attribute contract ----------

    [Fact]
    public void Get_endpoint_is_routed_at_me_billing_preference_as_billing_view()
    {
        Assert.Equal("me/billing-preference", GetAction.GetCustomAttribute<HttpMethodAttribute>()!.Template);
        Assert.Equal(
            HasPermissionAttribute.PolicyPrefix + SubscriptionPermissions.BillingView,
            GetAction.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
        Assert.Contains(ActorType.TenantAdmin, GetAction.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes);
        Assert.Contains(AccessSurface.Account, GetAction.GetCustomAttribute<AllowSurfaceAttribute>()!.Surfaces);
    }

    [Fact]
    public void Set_endpoint_is_routed_at_me_billing_preference_as_plan_change()
    {
        Assert.Equal("me/billing-preference", SetAction.GetCustomAttribute<HttpMethodAttribute>()!.Template);
        Assert.Equal(
            HasPermissionAttribute.PolicyPrefix + SubscriptionPermissions.PlanChange,
            SetAction.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
        Assert.Contains(ActorType.TenantAdmin, SetAction.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes);
        Assert.Contains(AccessSurface.Account, SetAction.GetCustomAttribute<AllowSurfaceAttribute>()!.Surfaces);
    }

    [Fact]
    public void Set_endpoint_requires_recent_authentication_for_step_up()
    {
        // Autorizar cobros futuros sin interacción es un side-effect legal — tiene que requerir reauth.
        Assert.NotNull(SetAction.GetCustomAttribute<RequireRecentAuthenticationAttribute>());
    }

    [Fact]
    public void Set_endpoint_is_rate_limited_as_management_not_as_read()
    {
        var policy = SetAction.GetCustomAttribute<RateLimitAttribute>()!.PolicyName;
        var category = RateLimitPolicyCatalog.GetByName(policy).Category;
        Assert.Equal(RateLimitCategory.G, category);
    }

    // ---------- Handler-level validation ----------

    [Fact]
    public async Task Automatic_without_consent_text_fails_with_400_code()
    {
        var result = await SetBillingPreferenceHandler.Handle(
            Command(mode: "Automatic", consentText: null, ip: "203.0.113.5"),
            new FakeRepo(existing: null),
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
    public async Task Automatic_without_captured_ip_fails_with_400_code()
    {
        var result = await SetBillingPreferenceHandler.Handle(
            Command(mode: "Automatic", consentText: "I authorize automatic charges.", ip: null),
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
        Assert.Equal("BillingPreference.MissingConsentIp", result.Error.Code);
    }

    [Fact]
    public async Task Manual_without_consent_text_is_allowed()
    {
        // Manual no exige consent ni plan vigente: el usuario sigue pagando con interacción cada ciclo.
        var result = await SetBillingPreferenceHandler.Handle(
            Command(mode: "Manual", consentText: null, ip: null, providerCode: null),
            new FakeRepo(null),
            new FakeSubscriptionRepo(subscription: null),
            new FakePlanRepo(plan: null),
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<TenantBillingPreference>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Valid_automatic_creates_preference_mandate_and_publishes_integration_event()
    {
        var tenantId = Guid.NewGuid();
        var (subscription, plan) = ActiveSubscriptionWithMonthlyPrice(tenantId, 29m);
        var repo = new FakeRepo(existing: null);
        var bus = new CapturingMessageBus();

        var result = await SetBillingPreferenceHandler.Handle(
            Command(
                mode: "Automatic",
                consentText: "I authorize automatic charges.",
                ip: "203.0.113.5",
                tenantId: tenantId
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
        Assert.NotNull(repo.Added.ActiveMandate());
        var evt = Assert.Single(bus.Published.OfType<TenantBillingPreferenceChangedIntegrationEvent>());
        Assert.Equal("Automatic", evt.Mode);
        Assert.True(evt.HasActiveMandate);
        Assert.Equal(2900, evt.AgreedAmountCents);
    }

    // ---------- Helpers ----------

    private static SetBillingPreferenceCommand Command(
        string mode,
        string? consentText,
        string? ip,
        string? providerCode = "Stripe",
        Guid? tenantId = null
    ) =>
        new(
            TenantId: tenantId ?? Guid.NewGuid(),
            ActorUserId: Guid.NewGuid(),
            Mode: mode,
            ProviderCode: providerCode,
            ConsentText: consentText,
            ConsentTextVersion: "v1-2026-10",
            ConsentIpAddress: ip,
            ConsentUserAgent: "xunit"
        );

    private static (TenantSubscription Subscription, SubscriptionPlan Plan) ActiveSubscriptionWithMonthlyPrice(
        Guid tenantId,
        decimal monthlyUsd
    )
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("pro").Value, "pro", "pro plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly])
            .Value;
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(monthlyUsd, "USD").Value).Value
        );
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        Assert.True(plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow).IsSuccess);

        var now = DateTime.UtcNow;
        var subscription = TenantSubscription
            .ActivateImmediately(tenantId, plan, version, BillingCycle.Monthly, now, now.AddDays(30), Guid.Empty, now)
            .Value;
        return (subscription, plan);
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
