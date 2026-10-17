using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Persistence.Converters;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class CatalogRedesignGuardrailsTests
{
    [Fact]
    public void PlanTier_uses_Starter_not_Standard()
    {
        var names = Enum.GetNames<PlanTier>();

        Assert.Contains("Starter", names);
        Assert.DoesNotContain("Standard", names);
    }

    [Fact]
    public void Storage_addons_declare_a_mutex_group()
    {
        var storage = QuotaAddOnCatalog.All.Where(addOn => addOn.EntitlementKey == "storage.max_bytes");

        Assert.All(storage, addOn => Assert.False(string.IsNullOrWhiteSpace(addOn.MutexGroup)));
    }

    [Fact]
    public void Module_and_quota_addon_codes_do_not_overlap()
    {
        var moduleCodes = SubscriptionModuleCatalogSeeder
            .Definitions.Where(module => module.Category == ModuleCategory.Commercial)
            .Select(module => $"module-addon-{module.Code}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var quotaCodes = QuotaAddOnCatalog.All.Select(addOn => addOn.Code);

        Assert.Empty(quotaCodes.Intersect(moduleCodes, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Quota_addons_do_not_declare_module_entitlements()
    {
        Assert.All(
            QuotaAddOnCatalog.All,
            addOn => Assert.False(addOn.EntitlementKey.StartsWith("module.", StringComparison.Ordinal))
        );
    }

    [Theory]
    [InlineData("Standard", PlanTier.Starter)]
    [InlineData("Starter", PlanTier.Starter)]
    [InlineData("starter", PlanTier.Starter)]
    [InlineData("Pro", PlanTier.Pro)]
    [InlineData("Enterprise", PlanTier.Enterprise)]
    [InlineData("Trial", PlanTier.Trial)]
    public void PlanTier_converter_reads_legacy_Standard_as_Starter(string raw, PlanTier expected)
    {
        Assert.Equal(expected, PlanTierConverter.FromRaw(raw));
    }
}
