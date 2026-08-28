using BuildingBlocks.Persistence;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;
using Xunit;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Regresión del bug de EF: agregar un hijo (color/asset) con PK Guid ya asignada a un aggregate
/// YA cargado y trackeado se trataba como Modified (UPDATE 0 filas → DbUpdateConcurrencyException)
/// en vez de Added (INSERT). El fix es <c>ValueGeneratedNever()</c> en las PK (guardrail #10). Corre
/// contra SQL real vía la factory.
/// </summary>
public sealed class TenantBrandPersistenceTests : IClassFixture<TenantApiFactory>
{
    private readonly TenantApiFactory factory;

    public TenantBrandPersistenceTests(TenantApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Adding_an_asset_and_a_color_to_a_loaded_brand_inserts_them()
    {
        var tenantId = Guid.NewGuid();

        // 1) Crear el brand vacío y guardarlo.
        await InScopeAsync(
            async (repo, uow) =>
            {
                var brand = TenantBrand.Create(tenantId, BrandSurface.Crm);
                await repo.AddAsync(brand);
                await uow.SaveChangesAsync();
            }
        );

        // 2) Cargarlo (ya trackeado desde la DB) y agregarle un asset + un color, guardar.
        //    Sin ValueGeneratedNever esto lanzaba DbUpdateConcurrencyException.
        await InScopeAsync(
            async (repo, uow) =>
            {
                var brand = await repo.GetAsync(tenantId, BrandSurface.Crm);
                Assert.NotNull(brand);
                var setAsset = brand!.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null);
                Assert.True(setAsset.IsSuccess);
                var setColor = brand.SetColor(BrandColorToken.Primary, "#1E466B");
                Assert.True(setColor.IsSuccess);
                await uow.SaveChangesAsync();
            }
        );

        // 3) Recargar y verificar que persistieron.
        await InScopeAsync(
            async (repo, _) =>
            {
                var brand = await repo.GetAsync(tenantId, BrandSurface.Crm);
                Assert.NotNull(brand);
                Assert.Single(brand!.Assets);
                Assert.Single(brand.Colors);
            }
        );
    }

    private async Task InScopeAsync(Func<ITenantBrandRepository, IUnitOfWork, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITenantBrandRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await action(repo, uow);
    }
}
