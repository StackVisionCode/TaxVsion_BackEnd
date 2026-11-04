using System.Linq;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Infrastructure.Persistence.Repositories;

/// <summary>
/// <c>IgnoreQueryFilters()</c> + tenantId explícito (guardrail #8): la marca es <c>ITenantOwned</c> y
/// el filtro global fail-closed devolvería cero filas en un scope sin tenant ambiental (consumers de
/// Wolverine). Siempre trae Colors y Assets — el agregado se lee completo.
/// </summary>
public sealed class TenantBrandRepository(TenantDbContext db) : ITenantBrandRepository
{
    public Task<TenantBrand?> GetAsync(Guid tenantId, BrandSurface surface, CancellationToken ct = default) =>
        db
            .TenantBrands.IgnoreQueryFilters()
            .Include(b => b.Colors)
            .Include(b => b.Assets)
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Surface == surface, ct);

    public async Task<IReadOnlyList<TenantBrand>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        await db
            .TenantBrands.IgnoreQueryFilters()
            .Include(b => b.Colors)
            .Include(b => b.Assets)
            .Where(b => b.TenantId == tenantId)
            .ToListAsync(ct);

    public async Task AddAsync(TenantBrand brand, CancellationToken ct = default) =>
        await db.TenantBrands.AddAsync(brand, ct);

    public Task<TenantBrandAsset?> GetConfirmedAssetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        db.Set<TenantBrandAsset>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.FileId == fileId && a.Status == BrandAssetStatus.Confirmed, ct);

    public Task<TenantBrand?> GetByAssetFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        db
            .TenantBrands.IgnoreQueryFilters()
            .Include(b => b.Assets)
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Assets.Any(a => a.FileId == fileId), ct);

    // Cruza tenants a propósito (republicación de plataforma): IgnoreQueryFilters en ambas tablas.
    public async Task<IReadOnlyList<ConfirmedBrandLogo>> ListConfirmedLogosAsync(
        int skip,
        int take,
        CancellationToken ct = default
    ) =>
        await db.Set<TenantBrandAsset>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.Key == BrandAssetKey.Logo && a.Status == BrandAssetStatus.Confirmed)
            .Join(
                db.TenantBrands.IgnoreQueryFilters(),
                asset => asset.TenantBrandId,
                brand => brand.Id,
                (asset, brand) => new { Asset = asset, brand.Surface }
            )
            .OrderBy(x => x.Asset.Id)
            .Skip(skip)
            .Take(take)
            .Select(x => new ConfirmedBrandLogo(
                x.Asset.TenantId,
                x.Surface,
                x.Asset.FileId,
                x.Asset.ContentType,
                x.Asset.SizeBytes,
                x.Asset.Width,
                x.Asset.Height,
                x.Asset.ConfirmedAtUtc ?? x.Asset.UpdatedAtUtc
            ))
            .ToListAsync(ct);
}
