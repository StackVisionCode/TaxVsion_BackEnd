using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Catalog.Domain.Categories;

/// <summary>Categoría del catálogo, tenant-owned, en árbol (self-reference vía <see cref="ParentCategoryId"/>).
/// Soft-delete.</summary>
public sealed class Category : TenantEntity
{
    public const int NameMax = 100;

    public Guid TaxUserId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public Guid? ParentCategoryId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    private Category() { }

    public static Result<Category> Create(
        Guid tenantId,
        Guid taxUserId,
        string name,
        string? description,
        Guid? parentCategoryId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Category>(CatalogErrors.InvalidTenant);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMax)
            return Result.Failure<Category>(CatalogErrors.InvalidName);

        var category = new Category
        {
            TaxUserId = taxUserId,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            ParentCategoryId = parentCategoryId == Guid.Empty ? null : parentCategoryId,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            IsDeleted = false,
        };
        category.SetTenant(tenantId);
        return Result.Success(category);
    }

    public Result Update(string name, string? description, Guid? parentCategoryId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMax)
            return Result.Failure(CatalogErrors.InvalidName);
        if (parentCategoryId == Id)
            return Result.Failure(new Error("catalog.categoryCycle", "A category cannot be its own parent."));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ParentCategoryId = parentCategoryId == Guid.Empty ? null : parentCategoryId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public void SetActive(bool active, DateTime nowUtc)
    {
        IsActive = active;
        UpdatedAtUtc = nowUtc;
    }

    public void SoftDelete(DateTime nowUtc)
    {
        if (IsDeleted)
            return;
        IsDeleted = true;
        DeletedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }
}
