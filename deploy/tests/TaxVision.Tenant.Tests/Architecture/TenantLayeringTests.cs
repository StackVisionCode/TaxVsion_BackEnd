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
}
