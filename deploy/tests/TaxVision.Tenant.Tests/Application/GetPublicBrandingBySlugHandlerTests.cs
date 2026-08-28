using System.Collections.Generic;
using System.Linq;
using BuildingBlocks.Tenancy;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Brands.Queries;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Application;

public sealed class GetPublicBrandingBySlugHandlerTests
{
    [Fact]
    public async Task Unknown_slug_returns_the_system_brand_without_asset_urls()
    {
        var tenantRepo = new FakeTenantRepo(); // no encuentra el slug
        var brandRepo = new FakeBrandRepo();
        // Marca del sistema sembrada: platform tiene primary/accent, sin assets.
        brandRepo.Seed(
            PlatformTenant.Id,
            BrandSurface.Crm,
            b =>
            {
                b.SetColor(BrandColorToken.Primary, "#1E466B");
                b.SetColor(BrandColorToken.Accent, "#67BAF4");
            }
        );

        var result = await GetPublicBrandingBySlugHandler.Handle(
            new GetPublicBrandingBySlugQuery("does-not-exist", BrandSurface.Crm),
            tenantRepo,
            brandRepo,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("#1E466B", result.Value.Primary);
        Assert.Equal("#67BAF4", result.Value.Accent);
        Assert.Null(result.Value.LogoUrl);
        Assert.Null(result.Value.FaviconUrl);
    }

    [Fact]
    public async Task A_confirmed_tenant_logo_becomes_a_public_url()
    {
        var tenantId = Guid.NewGuid();
        var tenantRepo = new FakeTenantRepo { { "manfer", tenantId } };
        var brandRepo = new FakeBrandRepo();
        var fileId = Guid.NewGuid();
        brandRepo.Seed(
            tenantId,
            BrandSurface.Crm,
            b =>
            {
                b.SetColor(BrandColorToken.Primary, "#AA0000");
                b.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, 40, 40);
                b.ConfirmAsset(BrandAssetKey.Logo, fileId, "image/png", 100, 40, 40, DateTime.UtcNow);
            }
        );

        var result = await GetPublicBrandingBySlugHandler.Handle(
            new GetPublicBrandingBySlugQuery("manfer", BrandSurface.Crm),
            tenantRepo,
            brandRepo,
            CancellationToken.None
        );

        Assert.Equal("#AA0000", result.Value.Primary);
        Assert.Equal($"/tenants/branding/assets/{fileId}", result.Value.LogoUrl);
    }

    [Fact]
    public async Task A_pending_tenant_logo_is_not_exposed_publicly()
    {
        var tenantId = Guid.NewGuid();
        var tenantRepo = new FakeTenantRepo { { "manfer", tenantId } };
        var brandRepo = new FakeBrandRepo();
        brandRepo.Seed(
            tenantId,
            BrandSurface.Crm,
            b => b.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null)
        );

        var result = await GetPublicBrandingBySlugHandler.Handle(
            new GetPublicBrandingBySlugQuery("manfer", BrandSurface.Crm),
            tenantRepo,
            brandRepo,
            CancellationToken.None
        );

        // Pendiente de escaneo → NO se muestra en el login.
        Assert.Null(result.Value.LogoUrl);
    }

    // ----- Fakes -----

    private sealed class FakeTenantRepo : ITenantRepository, IEnumerable<KeyValuePair<string, Guid>>
    {
        private readonly Dictionary<string, Guid> _bySlug = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string slug, Guid id) => _bySlug[slug] = id;

        public Task<Guid?> GetIdBySubDomainAsync(string subdomain, CancellationToken ct = default) =>
            Task.FromResult(_bySlug.TryGetValue(subdomain.Trim(), out var id) ? id : (Guid?)null);

        public Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(TenantEntity entity, CancellationToken ct = default) => throw new NotSupportedException();

        public void Remove(TenantEntity entity) => throw new NotSupportedException();

        public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantEntity?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IEnumerator<KeyValuePair<string, Guid>> GetEnumerator() => _bySlug.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FakeBrandRepo : ITenantBrandRepository
    {
        private readonly List<TenantBrand> _brands = [];

        public void Seed(Guid tenantId, BrandSurface surface, Action<TenantBrand> configure)
        {
            var brand = TenantBrand.Create(tenantId, surface);
            configure(brand);
            _brands.Add(brand);
        }

        public Task<TenantBrand?> GetAsync(Guid tenantId, BrandSurface surface, CancellationToken ct = default) =>
            Task.FromResult(_brands.FirstOrDefault(b => b.TenantId == tenantId && b.Surface == surface));

        public Task<IReadOnlyList<TenantBrand>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantBrand>>(_brands.Where(b => b.TenantId == tenantId).ToList());

        public Task AddAsync(TenantBrand brand, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<TenantBrandAsset?> GetConfirmedAssetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantBrand?> GetByAssetFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
