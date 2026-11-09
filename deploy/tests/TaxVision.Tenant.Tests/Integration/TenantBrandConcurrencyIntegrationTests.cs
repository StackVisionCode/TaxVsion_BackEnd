using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Persistence.Repositories;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (user-secrets de Tenant.Api): el resultado del escaneo de un archivo viejo no
/// puede confirmar el archivo nuevo que otro guardado puso en el mismo asset. Limpia su marca al final.
/// </summary>
public sealed class TenantBrandConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _oldFileId = Guid.NewGuid();
    private readonly Guid _newFileId = Guid.NewGuid();
    private readonly string _connectionString =
        TenantIntegrationDatabase.ConnectionString;

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private TenantDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<TenantDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        var brand = TenantBrand.Create(_tenantId, BrandSurface.Crm, DateTime.UtcNow);
        Assert.True(brand.SetAssetPending(BrandAssetKey.Logo, _oldFileId, "image/png", 100, 10, 10, DateTime.UtcNow).IsSuccess);
        db.TenantBrands.Add(brand);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.TenantBrands.IgnoreQueryFilters().Where(b => b.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ScanResultForOldFile_AfterNewUpload_DoesNotConfirmNewFile()
    {
        // Contexto 1 (consumer del escaneo) lee la marca con el asset en FileId = A.
        await using var consumer = NewContext();
        var stale = await new TenantBrandRepository(consumer).GetAsync(_tenantId, BrandSurface.Crm);
        Assert.NotNull(stale);

        // Contexto 2 (upload) reemplaza el logo por B y guarda primero.
        await using (var upload = NewContext())
        {
            var fresh = await new TenantBrandRepository(upload).GetAsync(_tenantId, BrandSurface.Crm);
            Assert.True(fresh!.SetAssetPending(BrandAssetKey.Logo, _newFileId, "image/png", 200, 20, 20, DateTime.UtcNow).IsSuccess);
            await upload.SaveChangesAsync();
        }

        // El consumer confirma A sobre su copia vieja.
        Assert.True(
            stale!
                .ConfirmAsset(BrandAssetKey.Logo, _oldFileId, "image/png", 100, 10, 10, DateTime.UtcNow)
                .IsSuccess
        );
        await Assert.ThrowsAsync<ConflictException>(() => consumer.SaveChangesAsync());

        await using var read = NewContext();
        var asset = await read
            .Set<TenantBrandAsset>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(a => a.TenantId == _tenantId);
        Assert.Equal(_newFileId, asset.FileId);
        Assert.Equal(BrandAssetStatus.Pending, asset.Status);
    }
}
