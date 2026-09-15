using TaxVision.Subscription.Application.Entitlements;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Enforcement status-aware: los módulos del plan y de los add-ons se apagan cuando la base
/// no da acceso (Suspended/Cancelled/Expired/Draft) y se mantienen durante la morosidad (PastDue/GracePeriod).</summary>
public sealed class EntitlementSnapshotBuilderTests
{
    [Fact]
    public async Task Active_subscription_keeps_plan_modules_on_and_merges_active_addons()
    {
        var snapshot = await BuildAsync(SubscriptionStatus.Active, withActiveAddOn: true);

        Assert.Equal("True", ModuleValue(snapshot, "module.documents"));
        Assert.Equal("true", ModuleValue(snapshot, "module.email")); // add-on fusionado
    }

    [Theory]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.GracePeriod)]
    public async Task Dunning_window_keeps_modules_on(SubscriptionStatus status)
    {
        var snapshot = await BuildAsync(status, withActiveAddOn: true);

        Assert.Equal("True", ModuleValue(snapshot, "module.documents"));
        Assert.Equal("true", ModuleValue(snapshot, "module.email"));
    }

    [Theory]
    [InlineData(SubscriptionStatus.Suspended)]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    public async Task No_access_status_turns_plan_modules_off_and_drops_addon_modules(SubscriptionStatus status)
    {
        var snapshot = await BuildAsync(status, withActiveAddOn: true);

        Assert.Equal("False", ModuleValue(snapshot, "module.documents")); // módulo del plan apagado
        Assert.Null(ModuleValue(snapshot, "module.email")); // add-on no fusionado (dependiente)
        // El límite del plan se conserva (el gate es por módulo, no por cuota).
        Assert.Equal("3", ModuleValue(snapshot, "seats.max"));
    }

    private static string? ModuleValue(TenantEntitlementSnapshot snapshot, string key)
    {
        foreach (var entry in snapshot.Entries)
            if (entry.Key.Value == key)
                return entry.Value;
        return null;
    }

    private static async Task<TenantEntitlementSnapshot> BuildAsync(SubscriptionStatus status, bool withActiveAddOn)
    {
        var tenantId = Guid.NewGuid();
        var plan = BuildPublishedPlan();
        var subscription = BuildSubscription(tenantId, plan, status);

        var addOnDefs = new FakeAddOnDefinitionRepository();
        var addOns = new List<TenantAddOn>();
        if (withActiveAddOn)
        {
            var (def, addOn) = BuildActiveModuleAddOn(tenantId);
            addOnDefs = new FakeAddOnDefinitionRepository(def);
            addOns.Add(addOn);
        }

        var result = await EntitlementSnapshotBuilder.BuildAsync(
            tenantId,
            new FakeSubscriptionRepo(subscription),
            new FakePlanRepo(plan),
            new FakeSeatRepo(),
            new FakeTenantAddOnRepo(addOns),
            addOnDefs,
            new FakeSnapshotRepo(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static SubscriptionPlan BuildPublishedPlan()
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "Starter plan", PlanTier.Standard, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        version.AddEntitlementDefinition(
            PlanEntitlementDefinition
                .Create(
                    version.Id,
                    EntitlementKey.Create("seats.max").Value,
                    EntitlementValueType.Int,
                    "3",
                    "seats.max"
                )
                .Value
        );
        version.AddFeature(
            PlanFeature
                .Create(version.Id, EntitlementKey.Create("module.documents").Value, true, "module.documents")
                .Value
        );
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(49m, "USD").Value).Value
        );
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        return plan;
    }

    private static TenantSubscription BuildSubscription(Guid tenantId, SubscriptionPlan plan, SubscriptionStatus status)
    {
        var nowUtc = DateTime.UtcNow;
        var subscription = TenantSubscription
            .ActivateImmediately(
                tenantId,
                plan,
                plan.GetPublishedVersion()!,
                BillingCycle.Monthly,
                nowUtc.AddDays(-10),
                nowUtc.AddDays(20),
                Guid.Empty,
                nowUtc
            )
            .Value;

        switch (status)
        {
            case SubscriptionStatus.Active:
                break;
            case SubscriptionStatus.PastDue:
                subscription.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, nowUtc);
                break;
            case SubscriptionStatus.GracePeriod:
                subscription.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, nowUtc);
                subscription.EnterGracePeriodAfterRetriesExhausted(nowUtc.AddDays(7), Guid.Empty, nowUtc);
                break;
            case SubscriptionStatus.Suspended:
                subscription.SuspendForPolicyViolation("test", Guid.Empty, nowUtc);
                break;
            case SubscriptionStatus.Cancelled:
                subscription.CancelImmediately("test", Guid.Empty, nowUtc);
                break;
            case SubscriptionStatus.Expired:
                subscription.CancelImmediately("test", Guid.Empty, nowUtc);
                subscription.ExpireAfterCancellationPeriodEnded(Guid.Empty, nowUtc);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "unsupported in test");
        }

        Assert.Equal(status, subscription.Status);
        return subscription;
    }

    private static (AddOnDefinition Definition, TenantAddOn AddOn) BuildActiveModuleAddOn(Guid tenantId)
    {
        var nowUtc = DateTime.UtcNow;
        var def = AddOnDefinition
            .Create(
                AddOnCode.Create("email.addon").Value,
                "Email",
                "Email",
                "module",
                false,
                [BillingCycle.Monthly],
                Guid.Empty,
                nowUtc
            )
            .Value;
        def.AddFeature(AddOnFeature.Create(def.Id, EntitlementKey.Create("module.email").Value, true).Value);
        def.Publish(Guid.Empty, nowUtc);

        var addOn = TenantAddOn
            .Purchase(tenantId, def, 1, Money.Zero("USD"), BillingCycle.Monthly, true, Guid.Empty, nowUtc)
            .Value;
        return (def, addOn);
    }
}
