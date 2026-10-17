using TaxVision.Subscription.Application.Plans;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class PlanModuleSelectionTests
{
    [Fact]
    public async Task Adds_all_core_modules_to_the_commercial_selection()
    {
        var modules = BuildCatalog();
        var commercial = modules.First(module => module.Category == ModuleCategory.Commercial);

        var result = await PlanModuleSelection.ComposeAsync(
            [commercial.Code],
            new FakeModuleRepo([.. modules]),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains(commercial.Code, result.Value);
        Assert.All(
            modules.Where(module => module.Category == ModuleCategory.Core),
            module => Assert.Contains(module.Code, result.Value)
        );
    }

    [Fact]
    public async Task Rejects_a_core_module_in_the_explicit_selection()
    {
        var modules = BuildCatalog();
        var core = modules.First(module => module.Category == ModuleCategory.Core);

        var result = await PlanModuleSelection.ComposeAsync(
            [core.Code],
            new FakeModuleRepo([.. modules]),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.CoreModuleCannotBeExplicit", result.Error.Code);
    }

    [Fact]
    public async Task Rejects_an_unknown_module()
    {
        var result = await PlanModuleSelection.ComposeAsync(
            ["missing-module"],
            new FakeModuleRepo([.. BuildCatalog()]),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Plan.UnknownModule", result.Error.Code);
    }

    private static IReadOnlyList<Module> BuildCatalog() =>
        SubscriptionModuleCatalogSeeder.Definitions.Select(BuildModule).ToArray();

    private static Module BuildModule(ModuleSeedDefinition definition)
    {
        var module = Module.Seed(definition.Id, definition.Code, definition.Name, definition.Description).Value;
        if (definition.Category == ModuleCategory.Core)
            return module;

        module.MarkAsCommercial();
        if (definition.MonthlyUsd is not null && definition.YearlyUsd is not null)
            module.SetAsAddon(definition.MonthlyUsd.Value, definition.YearlyUsd.Value);
        return module;
    }
}
