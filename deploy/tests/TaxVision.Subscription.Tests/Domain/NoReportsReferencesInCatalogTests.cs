using BuildingBlocks.Authorization;
using TaxVision.Subscription.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class NoReportsReferencesInCatalogTests
{
    private const string RetiredModuleCode = "reports";

    [Fact]
    public void Retired_module_is_absent_from_the_seed_and_permission_gate()
    {
        Assert.DoesNotContain(
            SubscriptionModuleCatalogSeeder.Definitions,
            module => string.Equals(module.Code, RetiredModuleCode, StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(RetiredModuleCode, PermissionModuleMap.KnownModules);
        Assert.Null(PermissionModuleMap.ModuleFor($"{RetiredModuleCode}.view"));
    }
}
