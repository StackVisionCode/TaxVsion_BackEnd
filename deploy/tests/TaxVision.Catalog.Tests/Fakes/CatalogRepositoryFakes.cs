using TaxVision.Catalog.Application.Abstractions;
using TaxVision.Catalog.Domain.Categories;
using TaxVision.Catalog.Domain.Items;

namespace TaxVision.Catalog.Tests.Fakes;

internal sealed class FakeCatalogItemRepository : ICatalogItemRepository
{
    public List<CatalogItem> Store { get; } = [];
    public List<CatalogItem> Added { get; } = [];

    public void Seed(CatalogItem item) => Store.Add(item);

    public Task<CatalogItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Store.Concat(Added).FirstOrDefault(i => i.TenantId == tenantId && !i.IsDeleted && i.Id == id));

    public Task<bool> SkuExistsAsync(Guid tenantId, string sku, Guid? excludeId, CancellationToken ct = default) =>
        Task.FromResult(
            Store
                .Concat(Added)
                .Any(i =>
                    i.TenantId == tenantId && !i.IsDeleted && i.Sku == sku && (excludeId == null || i.Id != excludeId)
                )
        );

    public Task<(IReadOnlyList<CatalogItem> Items, int Total)> ListAsync(
        Guid tenantId,
        Guid? categoryId,
        string? search,
        bool activeOnly,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var q = Store.Concat(Added).Where(i => i.TenantId == tenantId && !i.IsDeleted);
        if (categoryId is { } cid)
            q = q.Where(i => i.CategoryId == cid);
        if (activeOnly)
            q = q.Where(i => i.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(i => i.Name.Contains(search));
        var all = q.OrderBy(i => i.Name).ToList();
        IReadOnlyList<CatalogItem> paged = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((paged, all.Count));
    }

    public Task AddAsync(CatalogItem item, CancellationToken ct = default)
    {
        Added.Add(item);
        return Task.CompletedTask;
    }
}

internal sealed class FakeCategoryRepository : ICategoryRepository
{
    public List<Category> Store { get; } = [];
    public List<Category> Added { get; } = [];
    public bool HasChildrenResult { get; set; }

    public void Seed(Category category) => Store.Add(category);

    public Task<Category?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(Store.Concat(Added).FirstOrDefault(c => c.TenantId == tenantId && !c.IsDeleted && c.Id == id));

    public Task<bool> ExistsAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default) =>
        Task.FromResult(Store.Concat(Added).Any(c => c.TenantId == tenantId && !c.IsDeleted && c.Id == categoryId));

    public Task<bool> HasChildrenAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default) =>
        Task.FromResult(HasChildrenResult);

    public Task<IReadOnlyList<Category>> ListAsync(Guid tenantId, bool activeOnly, CancellationToken ct = default)
    {
        var q = Store.Concat(Added).Where(c => c.TenantId == tenantId && !c.IsDeleted);
        if (activeOnly)
            q = q.Where(c => c.IsActive);
        IReadOnlyList<Category> result = q.OrderBy(c => c.Name).ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(Category category, CancellationToken ct = default)
    {
        Added.Add(category);
        return Task.CompletedTask;
    }
}
