using System.Collections.Generic;
using System.Linq;
using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using TaxVision.Tenant.Application.Brands;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Tests.Application;

public sealed class TenantBrandColorHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task UpdateColors_creates_the_brand_lazily_on_first_use()
    {
        var repo = new FakeBrandRepository();
        var uow = new FakeUnitOfWork();
        var cache = new RecordingCache();

        var result = await UpdateTenantBrandColorsHandler.Handle(
            new UpdateTenantBrandColorsCommand(TenantId, BrandSurface.Crm, "#1E466B", "#67BAF4"),
            repo,
            uow,
            cache,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var brand = Assert.Single(repo.All);
        Assert.Equal(2, brand.Colors.Count);
        Assert.Equal(1, uow.SaveChangesCallCount);
        Assert.Contains(BrandCacheKeys.Brand(TenantId, BrandSurface.Crm), cache.Removed);
    }

    [Fact]
    public async Task UpdateColors_upserts_without_duplicating()
    {
        var repo = new FakeBrandRepository();
        repo.Seed(TenantId, BrandSurface.Crm, b => b.SetColor(BrandColorToken.Primary, "#1E466B"));

        await UpdateTenantBrandColorsHandler.Handle(
            new UpdateTenantBrandColorsCommand(TenantId, BrandSurface.Crm, "#0F5132", null),
            repo,
            new FakeUnitOfWork(),
            new RecordingCache(),
            CancellationToken.None
        );

        var brand = Assert.Single(repo.All);
        var primary = Assert.Single(brand.Colors);
        Assert.Equal("#0F5132", primary.Color.Value);
    }

    [Fact]
    public async Task UpdateColors_with_null_token_reverts_that_token_only()
    {
        var repo = new FakeBrandRepository();
        repo.Seed(
            TenantId,
            BrandSurface.Crm,
            b =>
            {
                b.SetColor(BrandColorToken.Primary, "#1E466B");
                b.SetColor(BrandColorToken.Accent, "#67BAF4");
            }
        );

        await UpdateTenantBrandColorsHandler.Handle(
            new UpdateTenantBrandColorsCommand(TenantId, BrandSurface.Crm, null, "#67BAF4"),
            repo,
            new FakeUnitOfWork(),
            new RecordingCache(),
            CancellationToken.None
        );

        var brand = Assert.Single(repo.All);
        var remaining = Assert.Single(brand.Colors);
        Assert.Equal(BrandColorToken.Accent, remaining.Token);
    }

    [Fact]
    public async Task UpdateColors_is_atomic_on_invalid_hex_nothing_applied()
    {
        var repo = new FakeBrandRepository();
        var uow = new FakeUnitOfWork();

        var result = await UpdateTenantBrandColorsHandler.Handle(
            new UpdateTenantBrandColorsCommand(TenantId, BrandSurface.Crm, "not-a-color", "#67BAF4"),
            repo,
            uow,
            new RecordingCache(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Empty(repo.All); // ni siquiera se creó la marca
        Assert.Equal(0, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task ResetColors_on_a_missing_brand_is_a_noop_success()
    {
        var repo = new FakeBrandRepository();
        var uow = new FakeUnitOfWork();

        var result = await ResetTenantBrandColorsHandler.Handle(
            new ResetTenantBrandColorsCommand(TenantId, BrandSurface.Crm),
            repo,
            uow,
            new RecordingCache(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task ResetColors_clears_a_customized_brand()
    {
        var repo = new FakeBrandRepository();
        repo.Seed(TenantId, BrandSurface.Crm, b => b.SetColor(BrandColorToken.Primary, "#1E466B"));

        await ResetTenantBrandColorsHandler.Handle(
            new ResetTenantBrandColorsCommand(TenantId, BrandSurface.Crm),
            repo,
            new FakeUnitOfWork(),
            new RecordingCache(),
            CancellationToken.None
        );

        Assert.Empty(Assert.Single(repo.All).Colors);
    }

    // ----- Fakes -----

    private sealed class FakeBrandRepository : ITenantBrandRepository
    {
        public List<TenantBrand> All { get; } = [];

        public void Seed(Guid tenantId, BrandSurface surface, Action<TenantBrand> configure)
        {
            var brand = TenantBrand.Create(tenantId, surface);
            configure(brand);
            All.Add(brand);
        }

        public Task<TenantBrand?> GetAsync(Guid tenantId, BrandSurface surface, CancellationToken ct = default) =>
            Task.FromResult(All.FirstOrDefault(b => b.TenantId == tenantId && b.Surface == surface));

        public Task<IReadOnlyList<TenantBrand>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantBrand>>(All.Where(b => b.TenantId == tenantId).ToList());

        public Task AddAsync(TenantBrand brand, CancellationToken ct = default)
        {
            All.Add(brand);
            return Task.CompletedTask;
        }

        public Task<TenantBrandAsset?> GetConfirmedAssetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantBrand?> GetByAssetFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class RecordingCache : ICacheService
    {
        public List<string> Removed { get; } = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            Removed.Add(key);
            return Task.CompletedTask;
        }

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
