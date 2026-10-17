using TaxVision.Subscription.Application.AddOns.Queries;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class GetAvailableAddOnsForTenantHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Excludes_modules_already_in_the_plan_and_all_core_modules()
    {
        var modules = BuildCatalog();
        var included = modules.First(module => module.IsSellableAsAddon);
        var plan = AddOnTestCatalog.PlanWith("starter", [included.Code]);

        var result = await Handle(plan, modules);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Value, addOn => addOn.Code == included.AddOnCode);
        Assert.DoesNotContain(
            result.Value,
            addOn =>
                modules.Any(module => module.Category == ModuleCategory.Core && addOn.Modules.Contains(module.Code))
        );
    }

    [Fact]
    public async Task Includes_sellable_modules_missing_from_the_plan()
    {
        var modules = BuildCatalog();
        var missing = modules.First(module => module.IsSellableAsAddon);
        var core = modules.First(module => module.Category == ModuleCategory.Core);
        var plan = AddOnTestCatalog.PlanWith("starter", [core.Code]);

        var result = await Handle(plan, modules);

        var addOn = Assert.Single(result.Value, candidate => candidate.Code == missing.AddOnCode);
        Assert.Equal("module", addOn.Category);
        Assert.Equal(missing.AddonPriceMonthlyUsd, addOn.MonthlyUsd);
        Assert.Equal([missing.Code], addOn.Modules);
    }

    [Fact]
    public async Task Includes_published_quota_addons()
    {
        var modules = BuildCatalog();
        var plan = AddOnTestCatalog.PlanWith(
            "starter",
            [modules.First(module => module.Category == ModuleCategory.Core).Code]
        );
        var quota = SubscriptionAddOnCatalogSeeder.BuildQuotaDefinition(QuotaAddOnCatalog.All.First(), DateTime.UtcNow);

        var result = await Handle(plan, modules, quota);

        var addOn = Assert.Single(result.Value, candidate => candidate.Code == quota.Code.Value);
        Assert.Equal("quota", addOn.Category);
        Assert.Equal(QuotaAddOnCatalog.StorageMutexGroup, addOn.MutexGroup);
    }

    private static Task<BuildingBlocks.Results.Result<IReadOnlyList<AvailableAddOnResponse>>> Handle(
        TaxVision.Subscription.Domain.Plans.SubscriptionPlan plan,
        IReadOnlyList<Module> modules,
        params TaxVision.Subscription.Domain.AddOns.AddOnDefinition[] addOns
    ) =>
        GetAvailableAddOnsForTenantHandler.Handle(
            new GetAvailableAddOnsForTenantQuery(TenantId),
            new FakeSubscriptionRepo(AddOnTestCatalog.ActiveSubscription(TenantId, plan)),
            new FakePlanRepo(plan),
            new FakeModuleRepo([.. modules]),
            new FakeAddOnDefinitionRepository(addOns),
            CancellationToken.None
        );

    private static IReadOnlyList<Module> BuildCatalog() =>
        SubscriptionModuleCatalogSeeder
            .Definitions.Select(definition =>
            {
                var module = Module.Seed(definition.Id, definition.Code, definition.Name, definition.Description).Value;
                if (definition.Category == ModuleCategory.Commercial)
                {
                    module.MarkAsCommercial();
                    if (definition.MonthlyUsd is not null && definition.YearlyUsd is not null)
                        module.SetAsAddon(definition.MonthlyUsd.Value, definition.YearlyUsd.Value);
                }
                return module;
            })
            .ToArray();
}
