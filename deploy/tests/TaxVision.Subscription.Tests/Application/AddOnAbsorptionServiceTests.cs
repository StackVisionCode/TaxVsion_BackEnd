using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.AddOns;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class AddOnAbsorptionServiceTests
{
    [Fact]
    public async Task Absorbs_a_module_addon_the_plan_now_covers_and_keeps_a_quota_addon()
    {
        var tenantId = Guid.NewGuid();
        var planVersion = PlanVersionWithModule("module.email");

        var (moduleDef, moduleAddOn) = ModuleAddOn(tenantId, "email.addon", "module.email");
        var (quotaDef, quotaAddOn) = QuotaAddOn(tenantId, "storage.extra");

        var bus = new CapturingMessageBus();

        await AddOnAbsorptionService.AbsorbCoveredByPlanAsync(
            tenantId,
            planVersion,
            new FakeTenantAddOnRepo([moduleAddOn, quotaAddOn]),
            new FakeAddOnDefinitionRepository(moduleDef, quotaDef),
            bus,
            new FakeSubscriptionMetrics(),
            "corr-1",
            actorUserId: Guid.NewGuid(),
            DateTime.UtcNow,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.Equal(AddOnStatus.Cancelled, moduleAddOn.Status);
        Assert.Equal("Absorbed by plan upgrade", moduleAddOn.CancellationReason);
        Assert.Equal(AddOnStatus.Active, quotaAddOn.Status); // la cuota sigue sumando → intacta

        var cancelled = Assert.Single(bus.Published.OfType<AddOnCancelledIntegrationEvent>());
        Assert.Equal("email.addon", cancelled.AddOnCode);
    }

    [Fact]
    public async Task Does_nothing_when_the_plan_does_not_cover_the_module()
    {
        var tenantId = Guid.NewGuid();
        var planVersion = PlanVersionWithModule("module.comms");
        var (def, addOn) = ModuleAddOn(tenantId, "email.addon", "module.email");
        var bus = new CapturingMessageBus();

        await AddOnAbsorptionService.AbsorbCoveredByPlanAsync(
            tenantId,
            planVersion,
            new FakeTenantAddOnRepo([addOn]),
            new FakeAddOnDefinitionRepository(def),
            bus,
            new FakeSubscriptionMetrics(),
            "corr-1",
            Guid.NewGuid(),
            DateTime.UtcNow,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.Equal(AddOnStatus.Active, addOn.Status);
        Assert.Empty(bus.Published.OfType<AddOnCancelledIntegrationEvent>());
    }

    private static SubscriptionPlanVersion PlanVersionWithModule(string moduleKey)
    {
        var version = SubscriptionPlanVersion.Create(Guid.NewGuid(), 1, 14, [BillingCycle.Monthly]).Value;
        version.AddFeature(
            PlanFeature.Create(version.Id, EntitlementKey.Create(moduleKey).Value, true, moduleKey).Value
        );
        return version;
    }

    private static (AddOnDefinition Definition, TenantAddOn AddOn) ModuleAddOn(
        Guid tenantId,
        string code,
        string moduleKey
    )
    {
        var nowUtc = DateTime.UtcNow;
        var def = AddOnDefinition
            .Create(
                AddOnCode.Create(code).Value,
                code,
                code,
                "module",
                false,
                [BillingCycle.Monthly],
                Guid.Empty,
                nowUtc
            )
            .Value;
        def.AddFeature(AddOnFeature.Create(def.Id, EntitlementKey.Create(moduleKey).Value, true).Value);
        def.Publish(Guid.Empty, nowUtc);

        var addOn = TenantAddOn
            .Purchase(tenantId, def, 1, Money.Create(29m, "USD").Value, BillingCycle.Monthly, true, Guid.Empty, nowUtc)
            .Value;
        return (def, addOn);
    }

    private static (AddOnDefinition Definition, TenantAddOn AddOn) QuotaAddOn(Guid tenantId, string code)
    {
        var nowUtc = DateTime.UtcNow;
        var def = AddOnDefinition
            .Create(
                AddOnCode.Create(code).Value,
                code,
                code,
                "quota",
                false,
                [BillingCycle.Monthly],
                Guid.Empty,
                nowUtc
            )
            .Value;
        def.AddEntitlementDefinition(
            AddOnEntitlementDefinition
                .Create(
                    def.Id,
                    EntitlementKey.Create("storage.max_bytes").Value,
                    EntitlementValueType.Long,
                    "100",
                    AddOnMergeStrategy.Sum
                )
                .Value
        );
        def.Publish(Guid.Empty, nowUtc);

        var addOn = TenantAddOn
            .Purchase(tenantId, def, 1, Money.Create(9m, "USD").Value, BillingCycle.Monthly, true, Guid.Empty, nowUtc)
            .Value;
        return (def, addOn);
    }
}
