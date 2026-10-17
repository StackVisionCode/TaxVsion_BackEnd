using BuildingBlocks.Authorization;
using TaxVision.Subscription.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class ModuleCatalogKnownModulesTests
{
    [Fact]
    public void Subscription_modules_match_the_permission_gate()
    {
        var catalogModules = SubscriptionModuleCatalogSeeder
            .Definitions.Select(module => module.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(catalogModules.SetEquals(PermissionModuleMap.KnownModules));
    }
}
