using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Catalog.Domain.ValueObjects;

namespace TaxVision.Catalog.Domain.Items;

/// <summary>
/// Un ítem del catálogo (producto o servicio) que un tenant vende/ofrece. Aggregate root, tenant-owned.
/// Precio y costo son multi-moneda (<see cref="Money"/>). El stock NO vive acá — lo maneja el servicio
/// Inventory (separado); este ítem solo declara <see cref="TrackInventory"/>. Soft-delete.
/// </summary>
public sealed class CatalogItem : TenantEntity
{
    public const int NameMax = 200;
    public const int SkuMax = 100;

    private readonly List<CatalogItemAttribute> _attributes = [];

    public Guid TaxUserId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public string? Sku { get; private set; }
    public string? Barcode { get; private set; }
    public Guid CategoryId { get; private set; }
    public ItemKind Kind { get; private set; }
    public Money Price { get; private set; } = default!;
    public Money? Cost { get; private set; }
    public string? Unit { get; private set; }
    public bool TrackInventory { get; private set; }
    public bool IsActive { get; private set; }
    public string? ImageUrl { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    public IReadOnlyCollection<CatalogItemAttribute> Attributes => _attributes;

    private CatalogItem() { }

    public static Result<CatalogItem> Create(
        Guid tenantId,
        Guid taxUserId,
        string name,
        string? description,
        string? sku,
        string? barcode,
        Guid categoryId,
        ItemKind kind,
        Money price,
        Money? cost,
        string? unit,
        bool trackInventory,
        string? imageUrl,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CatalogItem>(CatalogErrors.InvalidTenant);
        if (categoryId == Guid.Empty)
            return Result.Failure<CatalogItem>(CatalogErrors.InvalidCategory);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMax)
            return Result.Failure<CatalogItem>(CatalogErrors.InvalidName);

        var normalizedSku = Normalize(sku)?.ToUpperInvariant();
        if (normalizedSku is { Length: > SkuMax })
            return Result.Failure<CatalogItem>(CatalogErrors.InvalidSku);

        var item = new CatalogItem
        {
            TaxUserId = taxUserId,
            Name = name.Trim(),
            Description = Normalize(description),
            Sku = normalizedSku,
            Barcode = Normalize(barcode),
            CategoryId = categoryId,
            Kind = kind,
            Price = price,
            Cost = cost,
            Unit = Normalize(unit),
            // Un servicio nunca rastrea stock.
            TrackInventory = kind != ItemKind.Service && trackInventory,
            IsActive = true,
            ImageUrl = Normalize(imageUrl),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            IsDeleted = false,
        };
        item.SetTenant(tenantId);
        return Result.Success(item);
    }

    public Result Update(
        string name,
        string? description,
        string? barcode,
        Guid categoryId,
        string? unit,
        string? imageUrl,
        DateTime nowUtc
    )
    {
        if (categoryId == Guid.Empty)
            return Result.Failure(CatalogErrors.InvalidCategory);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMax)
            return Result.Failure(CatalogErrors.InvalidName);

        Name = name.Trim();
        Description = Normalize(description);
        Barcode = Normalize(barcode);
        CategoryId = categoryId;
        Unit = Normalize(unit);
        ImageUrl = Normalize(imageUrl);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public void ChangePrice(Money price, Money? cost, DateTime nowUtc)
    {
        Price = price;
        Cost = cost;
        UpdatedAtUtc = nowUtc;
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

    public void ReplaceAttributes(IEnumerable<(string Key, string Value, string? Type)> attributes)
    {
        _attributes.Clear();
        foreach (var (key, value, type) in attributes)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                continue;
            _attributes.Add(new CatalogItemAttribute(Id, key, value, type));
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
