using TaxVision.Subscription.Application.Subscriptions.Queries;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// Read model del Account. Lo que importa por estado: el plan que se muestra es el CONTRATADO, un add-on
/// que el plan ya trae no se puede comprar, y los asientos separan los del plan de los comprados.
/// </summary>
public sealed class GetAccountSubscriptionHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Returns_the_contracted_plan_not_the_newest_published_one()
    {
        var plan = PlanWith("pro", "Pro", monthlyPrice: 49m, seatsMax: 5, modules: ["documents"]);
        var contracted = plan.GetPublishedVersion()!;
        var subscription = ActiveSubscription(plan, contracted);
        // El catálogo sube de precio y de límites: el tenant sigue con lo que firmó.
        PublishNewerVersion(plan, monthlyPrice: 99m, seatsMax: 50);

        var result = await HandleAsync(plan, subscription);

        Assert.True(result.IsSuccess);
        Assert.Equal(4900, result.Value.Plan.CurrentCyclePriceCents);
        Assert.Equal("USD", result.Value.Plan.Currency);
        Assert.Equal(5, result.Value.Seats.IncludedInPlan);
        Assert.Equal("Active", result.Value.Plan.Status);
        Assert.False(result.Value.Plan.BillingAccessBlocked);
    }

    // Aceptación de la fase: en un plan que ya trae el módulo, el add-on no se ofrece.
    [Fact]
    public async Task An_add_on_whose_modules_the_plan_already_has_is_included()
    {
        var plan = PlanWith("enterprise", "Enterprise", monthlyPrice: 199m, seatsMax: 25, modules: ["email"]);
        var subscription = ActiveSubscription(plan, plan.GetPublishedVersion()!);

        var result = await HandleAsync(plan, subscription, addOns: [ModuleAddOn("email.addon", "email", monthly: 29m)]);

        var addOn = Assert.Single(result.Value.AddOns);
        Assert.Equal(AddOnEligibility.Included, addOn.Eligibility);
        Assert.Null(addOn.TenantAddOnId);
    }

    [Fact]
    public async Task An_add_on_the_plan_does_not_cover_is_available_with_its_catalog_price()
    {
        var plan = PlanWith("starter", "Starter", monthlyPrice: 29m, seatsMax: 3, modules: ["documents"]);
        var subscription = ActiveSubscription(plan, plan.GetPublishedVersion()!);

        var result = await HandleAsync(plan, subscription, addOns: [ModuleAddOn("email.addon", "email", monthly: 29m)]);

        var addOn = Assert.Single(result.Value.AddOns);
        Assert.Equal(AddOnEligibility.Available, addOn.Eligibility);
        Assert.Equal(2900, addOn.UnitAmountCents);
        Assert.Equal("USD", addOn.Currency);
    }

    [Fact]
    public async Task An_owned_add_on_is_active_and_shows_what_the_tenant_pays()
    {
        var plan = PlanWith("starter", "Starter", monthlyPrice: 29m, seatsMax: 3, modules: ["documents"]);
        var subscription = ActiveSubscription(plan, plan.GetPublishedVersion()!);
        var definition = ModuleAddOn("email.addon", "email", monthly: 29m);
        // Lo compró cuando costaba menos: la pantalla tiene que decir lo que paga, no el precio de hoy.
        var owned = TenantAddOn
            .Purchase(
                TenantId,
                definition,
                1,
                Money.Create(19m, "USD").Value,
                BillingCycle.Monthly,
                true,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        var result = await HandleAsync(plan, subscription, addOns: [definition], owned: [owned]);

        var addOn = Assert.Single(result.Value.AddOns);
        Assert.Equal(AddOnEligibility.Active, addOn.Eligibility);
        Assert.Equal(1900, addOn.UnitAmountCents);
        Assert.Equal(owned.Id, addOn.TenantAddOnId);
        Assert.True(addOn.AutoRenew);
    }

    [Fact]
    public async Task Counts_the_purchased_seats_apart_from_the_ones_the_plan_brings()
    {
        var plan = PlanWith("pro", "Pro", monthlyPrice: 49m, seatsMax: 5, modules: []);
        var subscription = ActiveSubscription(plan, plan.GetPublishedVersion()!);
        var seats = new CapturingSeatRepo();
        await seats.AddAsync(PurchasedSeat());
        var cancelled = PurchasedSeat();
        Assert.True(cancelled.CancelBeforeActivation("Not needed", Guid.Empty, DateTime.UtcNow).IsSuccess);
        await seats.AddAsync(cancelled);

        var result = await HandleAsync(plan, subscription, seats: seats);

        Assert.Equal(5, result.Value.Seats.IncludedInPlan);
        Assert.Equal(1, result.Value.Seats.Purchased); // el cancelado ya no se paga
        Assert.Equal(6, result.Value.Seats.Total);
    }

    [Fact]
    public async Task A_suspended_subscription_reports_the_access_as_blocked()
    {
        var plan = PlanWith("pro", "Pro", monthlyPrice: 49m, seatsMax: 5, modules: []);
        var subscription = ActiveSubscription(plan, plan.GetPublishedVersion()!);
        Assert.True(subscription.SuspendForPolicyViolation("Unpaid", Guid.Empty, DateTime.UtcNow).IsSuccess);

        var result = await HandleAsync(plan, subscription);

        Assert.Equal("Suspended", result.Value.Plan.Status);
        Assert.True(result.Value.Plan.BillingAccessBlocked);
    }

    [Fact]
    public async Task Fails_when_the_tenant_has_no_subscription()
    {
        var plan = PlanWith("pro", "Pro", monthlyPrice: 49m, seatsMax: 5, modules: []);

        var result = await GetAccountSubscriptionHandler.Handle(
            new GetAccountSubscriptionQuery(TenantId),
            new FakeSubscriptionRepo(subscription: null),
            new FakePlanRepo(plan),
            new CapturingSeatRepo(),
            new FakeAddOnDefinitionRepository(),
            new FakeTenantAddOnRepo(),
            new FakeSeatPurchaseIntentRepository(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.NotFound", result.Error.Code);
    }

    private static Task<BuildingBlocks.Results.Result<AccountSubscriptionResponse>> HandleAsync(
        SubscriptionPlan plan,
        TenantSubscription subscription,
        AddOnDefinition[]? addOns = null,
        TenantAddOn[]? owned = null,
        CapturingSeatRepo? seats = null
    ) =>
        GetAccountSubscriptionHandler.Handle(
            new GetAccountSubscriptionQuery(TenantId),
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            seats ?? new CapturingSeatRepo(),
            new FakeAddOnDefinitionRepository(addOns ?? []),
            new FakeTenantAddOnRepo(owned ?? []),
            new FakeSeatPurchaseIntentRepository(),
            CancellationToken.None
        );

    private static SubscriptionPlan PlanWith(
        string code,
        string name,
        decimal monthlyPrice,
        int seatsMax,
        string[] modules
    )
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, name, $"{name} plan", PlanTier.Standard, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        AddEntitlements(version, monthlyPrice, seatsMax, modules);
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        return plan;
    }

    private static void PublishNewerVersion(SubscriptionPlan plan, decimal monthlyPrice, int seatsMax)
    {
        var nowUtc = DateTime.UtcNow;
        var version = SubscriptionPlanVersion.Create(plan.Id, 2, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        AddEntitlements(version, monthlyPrice, seatsMax, modules: []);
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
    }

    private static void AddEntitlements(
        SubscriptionPlanVersion version,
        decimal monthlyPrice,
        int seatsMax,
        string[] modules
    )
    {
        version.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(
                    version.Id,
                    EntitlementKey.Create("seats.max").Value,
                    EntitlementValueType.Int,
                    seatsMax.ToString(),
                    "seats.max"
                )
                .Value
        );
        foreach (var module in modules)
        {
            version.AddFeature(
                PlanFeature.Create(version.Id, EntitlementKey.Create($"module.{module}").Value, true, module).Value
            );
        }
        version.AddPriceTier(
            PlanPriceTier
                .Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(monthlyPrice, "USD").Value)
                .Value
        );
    }

    private static TenantSubscription ActiveSubscription(SubscriptionPlan plan, SubscriptionPlanVersion version)
    {
        var nowUtc = DateTime.UtcNow;
        return TenantSubscription
            .ActivateImmediately(
                TenantId,
                plan,
                version,
                BillingCycle.Monthly,
                nowUtc,
                nowUtc.AddDays(30),
                Guid.Empty,
                nowUtc
            )
            .Value;
    }

    private static AddOnDefinition ModuleAddOn(string code, string module, decimal monthly)
    {
        var nowUtc = DateTime.UtcNow;
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create(code).Value,
                code,
                code,
                "module",
                false,
                [BillingCycle.Monthly, BillingCycle.Yearly],
                Guid.Empty,
                nowUtc
            )
            .Value;
        definition.AddFeature(
            AddOnFeature.Create(definition.Id, EntitlementKey.Create($"module.{module}").Value, true).Value
        );
        definition.AddPriceTier(
            AddOnPriceTier
                .Create(definition.Id, BillingCycle.Monthly, 1, null, Money.Create(monthly, "USD").Value)
                .Value
        );
        definition.Publish(Guid.Empty, nowUtc);
        return definition;
    }

    private static SubscriptionSeat PurchasedSeat() =>
        SubscriptionSeat
            .Purchase(
                TenantId,
                SeatType.Standard,
                SeatSourceType.Plan,
                sourceReferenceId: null,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
}
