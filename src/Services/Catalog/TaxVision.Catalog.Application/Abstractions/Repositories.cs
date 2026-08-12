using TaxVision.Catalog.Domain.Categories;
using TaxVision.Catalog.Domain.Items;

namespace TaxVision.Catalog.Application.Abstractions;

// Los reads toman tenantId EXPLÍCITO y usan IgnoreQueryFilters en la implementación: Wolverine corre
// el handler en un DI scope distinto del de la request HTTP, así que el TenantContext ambiente (y por
// ende el filtro global fail-closed) NO llega poblado al DbContext del handler. Patrón del monorepo
// (ver LocalCommandTenantMiddleware doc + CloudStorage/Sms). El caller ya trae el tenant validado del JWT.
public interface ICatalogItemRepository
{
    Task<CatalogItem?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Unicidad de SKU por tenant (pre-check; el índice único filtrado es la garantía real).</summary>
    Task<bool> SkuExistsAsync(Guid tenantId, string sku, Guid? excludeId, CancellationToken ct = default);

    /// <summary>Listado paginado, filtra por categoría, texto y activos.</summary>
    Task<(IReadOnlyList<CatalogItem> Items, int Total)> ListAsync(
        Guid tenantId,
        Guid? categoryId,
        string? search,
        bool activeOnly,
        int page,
        int pageSize,
        CancellationToken ct = default
    );

    Task AddAsync(CatalogItem item, CancellationToken ct = default);
}

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default);

    /// <summary>¿La categoría tiene subcategorías o ítems? (para no borrar categorías en uso).</summary>
    Task<bool> HasChildrenAsync(Guid tenantId, Guid categoryId, CancellationToken ct = default);

    Task<IReadOnlyList<Category>> ListAsync(Guid tenantId, bool activeOnly, CancellationToken ct = default);

    Task AddAsync(Category category, CancellationToken ct = default);
}
