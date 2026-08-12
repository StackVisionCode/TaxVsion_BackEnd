using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Inventory.Domain.Stock;

/// <summary>Nivel de stock por ítem del catálogo (referencia débil a <see cref="CatalogItemId"/>, sin FK
/// cross-service). Una fila por <c>(tenant, catalogItemId)</c>. Los movimientos ajustan la cantidad y
/// se registran en el ledger inmutable <see cref="StockMovement"/>.</summary>
public sealed class StockLevel : TenantEntity
{
    public Guid CatalogItemId { get; private set; }
    public int QuantityOnHand { get; private set; }
    public int MinLevel { get; private set; }
    public int MaxLevel { get; private set; }
    public int ReorderPoint { get; private set; }
    public bool IsTracked { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public bool IsLowStock => IsTracked && QuantityOnHand <= MinLevel;

    private StockLevel() { }

    public static Result<StockLevel> Create(
        Guid tenantId,
        Guid catalogItemId,
        int initialQuantity,
        int minLevel,
        int maxLevel,
        int reorderPoint,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<StockLevel>(InventoryErrors.InvalidTenant);
        if (catalogItemId == Guid.Empty)
            return Result.Failure<StockLevel>(InventoryErrors.InvalidCatalogItem);
        if (initialQuantity < 0)
            return Result.Failure<StockLevel>(InventoryErrors.InvalidQuantity);

        var level = new StockLevel
        {
            CatalogItemId = catalogItemId,
            QuantityOnHand = initialQuantity,
            MinLevel = Math.Max(0, minLevel),
            MaxLevel = Math.Max(0, maxLevel),
            ReorderPoint = Math.Max(0, reorderPoint),
            IsTracked = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        level.SetTenant(tenantId);
        return Result.Success(level);
    }

    public void SetThresholds(int minLevel, int maxLevel, int reorderPoint, DateTime nowUtc)
    {
        MinLevel = Math.Max(0, minLevel);
        MaxLevel = Math.Max(0, maxLevel);
        ReorderPoint = Math.Max(0, reorderPoint);
        UpdatedAtUtc = nowUtc;
    }

    public void SetTracked(bool tracked, DateTime nowUtc)
    {
        IsTracked = tracked;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Aplica un movimiento: Purchase/Return suman, Sale/Damaged restan, Adjustment/Transfer
    /// llevan un delta con signo. Rechaza dejar el stock negativo. Devuelve (cantidad previa, nueva).</summary>
    public Result<(int Previous, int New)> RegisterMovement(StockMovementType type, int quantity, DateTime nowUtc)
    {
        var previous = QuantityOnHand;
        var delta = type switch
        {
            StockMovementType.Purchase or StockMovementType.Return => Math.Abs(quantity),
            StockMovementType.Sale or StockMovementType.Damaged => -Math.Abs(quantity),
            StockMovementType.Adjustment or StockMovementType.Transfer => quantity,
            _ => 0,
        };

        var next = previous + delta;
        if (next < 0)
            return Result.Failure<(int, int)>(InventoryErrors.InsufficientStock);

        QuantityOnHand = next;
        UpdatedAtUtc = nowUtc;
        return Result.Success((previous, next));
    }
}
