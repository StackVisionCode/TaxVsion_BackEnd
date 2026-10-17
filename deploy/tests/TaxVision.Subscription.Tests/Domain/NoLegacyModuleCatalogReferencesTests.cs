using TaxVision.Subscription.Application.AddOns.Queries;
using TaxVision.Subscription.Domain.Catalog;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class NoLegacyModuleCatalogReferencesTests
{
    [Theory]
    [InlineData("ModuleAddOnCatalog")]
    [InlineData("PlanModuleCatalog")]
    public void Legacy_catalog_types_are_absent_from_subscription_domain(string legacyTypeName)
    {
        var legacyType = typeof(Module).Assembly.GetTypes().SingleOrDefault(type => type.Name == legacyTypeName);

        Assert.Null(legacyType);
    }

    [Fact]
    public void Legacy_addon_catalog_query_is_absent_from_application()
    {
        var queryTypes = typeof(GetAvailableAddOnsForTenantQuery).Assembly.GetTypes();

        Assert.DoesNotContain(queryTypes, type => type.Name is "GetAddOnCatalogQuery" or "GetAddOnCatalogHandler");
    }
}
