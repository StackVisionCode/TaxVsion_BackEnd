using Microsoft.EntityFrameworkCore;
using TaxVision.Catalog.Application.Abstractions;
using TaxVision.Catalog.Domain.Categories;
using TaxVision.Catalog.Domain.Items;

namespace TaxVision.Catalog.Infrastructure.Persistence.Repositories;

// Reads con IgnoreQueryFilters() + tenantId + !IsDeleted EXPLÍCITOS: el filtro global fail-closed no
// llega poblado al DbContext dentro del scope de un handler Wolverine (ver LocalCommandTenantMiddleware).
// El tenantId ya viene validado del JWT en el comando/query.
public sealed class CatalogItemRepository(CatalogDbContext db) : ICatalogItemRepository
{
    public Task<CatalogItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .CatalogItems.IgnoreQueryFilters()
            .Include(i => i.Attributes)
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && !i.IsDeleted && i.Id == id, ct);

    public Task<bool> SkuExistsAsync(Guid tenantId, string sku, Guid? excludeId, CancellationToken ct = default) =>
        db
            .CatalogItems.IgnoreQueryFilters()
            .AnyAsync(
                i => i.TenantId == tenantId && !i.IsDeleted && i.Sku == sku && (excludeId == null || i.Id != excludeId),
                ct
            );

    public async Task<(IReadOnlyList<CatalogItem> Items, int Total)> ListAsync(
        Guid tenantId,
        Guid? categoryId,
        string? search,
        bool activeOnly,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var query = db
            .CatalogItems.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && !i.IsDeleted);

        if (categoryId is { } cid)
            query = query.Where(i => i.CategoryId == cid);
        if (activeOnly)
            query = query.Where(i => i.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(i =>
                i.Name.Contains(term)
                || (i.Sku != null && i.Sku.Contains(term))
                || (i.Barcode != null && i.Barcode.Contains(term))
            );
        }

        var total = await query.CountAsync(ct);
        // Lista liviana: no incluye atributos (se ven en el GET individual).
        var rows = await query.OrderBy(i => i.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (rows, total);
    }

    public async Task AddAsync(CatalogItem item, CancellationToken ct = default) =>
        await db.CatalogItems.AddAsync(item, ct);
}

public sealed class CategoryRepository(CatalogDbContext db) : ICategoryRepository
{
    public Task<Category?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .Categories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && !c.IsDeleted && c.Id == id, ct);

    public Task<bool> ExistsAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default) =>
        db
            .Categories.IgnoreQueryFilters()
            .AnyAsync(c => c.TenantId == tenantId && !c.IsDeleted && c.Id == categoryId, ct);

    public async Task<bool> HasChildrenAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default) =>
        await db
            .Categories.IgnoreQueryFilters()
            .AnyAsync(c => c.TenantId == tenantId && !c.IsDeleted && c.ParentCategoryId == categoryId, ct)
        || await db
            .CatalogItems.IgnoreQueryFilters()
            .AnyAsync(i => i.TenantId == tenantId && !i.IsDeleted && i.CategoryId == categoryId, ct);

    public async Task<IReadOnlyList<Category>> ListAsync(Guid tenantId, bool activeOnly, CancellationToken ct = default)
    {
        var query = db
            .Categories.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && !c.IsDeleted);
        if (activeOnly)
            query = query.Where(c => c.IsActive);
        return await query.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task AddAsync(Category category, CancellationToken ct = default) =>
        await db.Categories.AddAsync(category, ct);
}
