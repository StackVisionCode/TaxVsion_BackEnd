using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class ModuleCatalogAvailabilityTests
{
    [Fact]
    public void Core_modules_have_no_addon_prices()
    {
        var modules = SubscriptionModuleCatalogSeeder.Definitions.Where(module =>
            module.Category == ModuleCategory.Core
        );

        Assert.All(
            modules,
            module =>
            {
                Assert.Null(module.MonthlyUsd);
                Assert.Null(module.YearlyUsd);
            }
        );
    }

    [Fact]
    public void Sellable_commercial_modules_have_both_prices()
    {
        var modules = SubscriptionModuleCatalogSeeder.Definitions.Where(module =>
            module.Category == ModuleCategory.Commercial && module.MonthlyUsd is not null
        );

        Assert.NotEmpty(modules);
        Assert.All(modules, module => Assert.True(module.YearlyUsd > 0m));
    }
}
