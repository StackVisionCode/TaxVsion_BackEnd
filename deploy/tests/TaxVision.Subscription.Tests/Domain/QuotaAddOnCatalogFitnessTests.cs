using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class QuotaAddOnCatalogFitnessTests
{
    [Fact]
    public void Storage_addons_share_the_storage_mutex_group()
    {
        var storage = QuotaAddOnCatalog.All.Where(addOn => addOn.EntitlementKey == "storage.max_bytes").ToArray();

        Assert.NotEmpty(storage);
        Assert.All(storage, addOn => Assert.Equal(QuotaAddOnCatalog.StorageMutexGroup, addOn.MutexGroup));
    }

    [Fact]
    public void Quota_addons_use_additive_merge_strategies()
    {
        Assert.All(
            QuotaAddOnCatalog.All,
            addOn => Assert.True(addOn.MergeStrategy is AddOnMergeStrategy.Sum or AddOnMergeStrategy.Max)
        );
    }

    [Fact]
    public void Storage_amounts_and_prices_increase_together()
    {
        var sizes = QuotaAddOnCatalog
            .All.Where(addOn => addOn.MutexGroup == QuotaAddOnCatalog.StorageMutexGroup)
            .OrderBy(addOn => long.Parse(addOn.EntitlementAmount))
            .ToArray();

        Assert.Equal(3, sizes.Length);
        Assert.True(sizes[0].MonthlyUsd < sizes[1].MonthlyUsd);
        Assert.True(sizes[1].MonthlyUsd < sizes[2].MonthlyUsd);
    }

    [Fact]
    public void Addon_codes_are_unique_across_module_and_quota_sources()
    {
        var quotaCodes = QuotaAddOnCatalog.All.Select(addOn => addOn.Code);
        var moduleCodes = SubscriptionModuleCatalogSeeder
            .Definitions.Where(module => module.Category == ModuleCategory.Commercial)
            .Select(module => $"module-addon-{module.Code}");

        Assert.Empty(quotaCodes.Intersect(moduleCodes, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Storage_entitlement_key_matches_the_downstream_contract()
    {
        var keys = QuotaAddOnCatalog
            .All.Where(addOn => addOn.MutexGroup == QuotaAddOnCatalog.StorageMutexGroup)
            .Select(addOn => addOn.EntitlementKey)
            .Distinct()
            .ToArray();

        Assert.Equal(["storage.max_bytes"], keys);
    }
}
