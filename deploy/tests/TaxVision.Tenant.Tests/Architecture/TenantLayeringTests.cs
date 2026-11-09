// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Tenant.Application.Brands;
using TaxVision.Tenant.Domain.Brands;

namespace TaxVision.Tenant.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class TenantLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(TenantBrand).Assembly;

    protected override Assembly ApplicationAssembly => typeof(BrandCacheKeys).Assembly;

    // TEN-9: el agregado nace por su fábrica; EF usa igual un constructor privado.
    [Fact]
    public void DomainAggregates_HaveNoPublicParameterlessConstructor()
    {
        var offenders = DomainAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => IsAggregate(type) && type.GetConstructor(Type.EmptyTypes) is not null)
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool IsAggregate(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name.EndsWith("AggregateRoot", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
