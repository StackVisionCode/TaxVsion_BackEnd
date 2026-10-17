using TaxVision.Subscription.Domain.Catalog;
using Xunit;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class ModuleTests
{
    [Fact]
    public void Create_defaults_to_core()
    {
        var module = Module.Create("customers", "Customers", "Customer directory.").Value;

        Assert.Equal(ModuleCategory.Core, module.Category);
        Assert.False(module.IsSellableAsAddon);
        Assert.Null(module.AddonPriceMonthlyUsd);
        Assert.Null(module.AddonPriceYearlyUsd);
    }

    [Fact]
    public void SetAsAddon_requires_commercial_category()
    {
        var module = Module.Create("customers", "Customers", "Customer directory.").Value;

        var result = module.SetAsAddon(29m, 290m);

        Assert.True(result.IsFailure);
        Assert.Equal("Module.CoreCannotBeAddon", result.Error.Code);
    }

    [Fact]
    public void MarkAsCore_clears_addon_pricing()
    {
        var module = Module.Create("email", "Email", "Office email.").Value;
        module.MarkAsCommercial();
        module.SetAsAddon(29m, 290m);

        module.MarkAsCore();

        Assert.Equal(ModuleCategory.Core, module.Category);
        Assert.False(module.IsSellableAsAddon);
        Assert.Null(module.AddonPriceMonthlyUsd);
        Assert.Null(module.AddonPriceYearlyUsd);
    }

    [Theory]
    [InlineData(0, 290)]
    [InlineData(29, 0)]
    [InlineData(-1, 290)]
    [InlineData(29, -1)]
    public void SetAsAddon_validates_positive_prices(decimal monthly, decimal yearly)
    {
        var module = Module.Create("email", "Email", "Office email.").Value;
        module.MarkAsCommercial();

        var result = module.SetAsAddon(monthly, yearly);

        Assert.True(result.IsFailure);
        Assert.Equal("Module.InvalidAddonPrice", result.Error.Code);
    }
}
