using TaxVision.Catalog.Domain.Categories;
using TaxVision.Catalog.Domain.Items;

namespace TaxVision.Catalog.Application.Common;

public sealed record MoneyDto(decimal Amount, string Currency);

public sealed record CatalogItemAttributeDto(string Key, string Value, string? ValueType);

public sealed record CatalogItemDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    string? Description,
    string? Sku,
    string? Barcode,
    string Kind,
    MoneyDto Price,
    MoneyDto? Cost,
    string? Unit,
    bool TrackInventory,
    bool IsActive,
    string? ImageUrl,
    IReadOnlyList<CatalogItemAttributeDto> Attributes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
);

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class CatalogMappings
{
    public static CatalogItemDto ToDto(this CatalogItem i) =>
        new(
            i.Id,
            i.CategoryId,
            i.Name,
            i.Description,
            i.Sku,
            i.Barcode,
            i.Kind.ToString(),
            new MoneyDto(i.Price.Amount, i.Price.Currency),
            i.Cost is null ? null : new MoneyDto(i.Cost.Amount, i.Cost.Currency),
            i.Unit,
            i.TrackInventory,
            i.IsActive,
            i.ImageUrl,
            i.Attributes.Select(a => new CatalogItemAttributeDto(a.Key, a.Value, a.ValueType)).ToList(),
            i.CreatedAtUtc,
            i.UpdatedAtUtc
        );

    public static CategoryDto ToDto(this Category c) =>
        new(c.Id, c.Name, c.Description, c.ParentCategoryId, c.IsActive, c.CreatedAtUtc, c.UpdatedAtUtc);
}
