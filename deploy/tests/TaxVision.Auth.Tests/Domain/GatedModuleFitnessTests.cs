using BuildingBlocks.Authorization;
using TaxVision.Auth.Domain.Roles;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

public sealed class GatedModuleFitnessTests
{
    [Fact]
    public void Permission_catalog_matches_the_runtime_module_map()
    {
        var mismatches = PermissionCatalog
            .All.Where(definition => definition.GatedModule != PermissionModuleMap.ModuleFor(definition.Code))
            .Select(definition => definition.Code)
            .ToArray();

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Every_gated_permission_uses_a_known_module()
    {
        var orphans = PermissionCatalog
            .All.Where(definition =>
                definition.GatedModule is not null && !PermissionModuleMap.KnownModules.Contains(definition.GatedModule)
            )
            .Select(definition => $"{definition.Code} -> {definition.GatedModule}")
            .ToArray();

        Assert.Empty(orphans);
    }
}
