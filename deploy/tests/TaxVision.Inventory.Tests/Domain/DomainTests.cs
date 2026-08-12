using TaxVision.Inventory.Domain;
using TaxVision.Inventory.Domain.Permissions;
using TaxVision.Inventory.Domain.Stock;
using TaxVision.Inventory.Domain.Suppliers;
using TaxVision.Inventory.Domain.ValueObjects;

namespace TaxVision.Inventory.Tests.Domain;

public sealed class StockLevelTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Item = Guid.NewGuid();

    private static StockLevel New(int qty = 10, int min = 3) =>
        StockLevel.Create(Tenant, Item, qty, min, 0, 0, Now).Value;

    [Fact]
    public void Purchase_and_return_add()
    {
        var s = New(10);
        Assert.Equal((10, 15), s.RegisterMovement(StockMovementType.Purchase, 5, Now).Value);
        Assert.Equal((15, 18), s.RegisterMovement(StockMovementType.Return, 3, Now).Value);
        Assert.Equal(18, s.QuantityOnHand);
    }

    [Fact]
    public void Sale_and_damaged_subtract()
    {
        var s = New(10);
        Assert.Equal((10, 6), s.RegisterMovement(StockMovementType.Sale, 4, Now).Value);
        Assert.Equal((6, 5), s.RegisterMovement(StockMovementType.Damaged, 1, Now).Value);
    }

    [Fact]
    public void Adjustment_is_signed_delta()
    {
        var s = New(10);
        Assert.Equal(7, s.RegisterMovement(StockMovementType.Adjustment, -3, Now).Value.New);
        Assert.Equal(12, s.RegisterMovement(StockMovementType.Adjustment, 5, Now).Value.New);
    }

    [Fact]
    public void Sale_beyond_stock_is_rejected()
    {
        var s = New(2);
        var r = s.RegisterMovement(StockMovementType.Sale, 5, Now);
        Assert.True(r.IsFailure);
        Assert.Equal(InventoryErrors.InsufficientStock.Code, r.Error.Code);
        Assert.Equal(2, s.QuantityOnHand); // unchanged
    }

    [Fact]
    public void IsLowStock_when_at_or_below_min()
    {
        Assert.True(New(3, 3).IsLowStock);
        Assert.True(New(2, 3).IsLowStock);
        Assert.False(New(4, 3).IsLowStock);
    }

    [Fact]
    public void Create_rejects_empty_item_and_negative_qty()
    {
        Assert.Equal(
            InventoryErrors.InvalidCatalogItem.Code,
            StockLevel.Create(Tenant, Guid.Empty, 0, 0, 0, 0, Now).Error.Code
        );
        Assert.Equal(
            InventoryErrors.InvalidQuantity.Code,
            StockLevel.Create(Tenant, Item, -1, 0, 0, 0, Now).Error.Code
        );
    }
}

public sealed class SupplierAndLinkTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Supplier_create_update_softdelete()
    {
        var s = Supplier.Create(Tenant, Guid.NewGuid(), "ACME", "Bob", "b@acme.co", null, null, "RNC1", Now).Value;
        Assert.True(s.IsActive);
        Assert.True(s.Update("ACME 2", null, null, null, null, null, Now).IsSuccess);
        Assert.Equal("ACME 2", s.Name);
        s.SoftDelete(Now);
        Assert.True(s.IsDeleted);
    }

    [Fact]
    public void Supplier_rejects_blank_name()
    {
        Assert.Equal(
            InventoryErrors.InvalidName.Code,
            Supplier.Create(Tenant, Guid.NewGuid(), "  ", null, null, null, null, null, Now).Error.Code
        );
    }

    [Fact]
    public void ItemSupplier_create_with_price()
    {
        var link = ItemSupplier
            .Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), "SUP-1", Money.Create(50, "USD").Value, 7, true, Now)
            .Value;
        Assert.Equal("USD", link.SupplierPrice!.Currency);
        Assert.Equal(7, link.LeadTimeDays);
        Assert.True(link.IsPreferred);
    }
}

public sealed class MoneyTests
{
    [Fact]
    public void Create_valid_and_invalid()
    {
        Assert.True(Money.Create(10, "usd").IsSuccess);
        Assert.Equal("USD", Money.Create(10, "usd").Value.Currency);
        Assert.True(Money.Create(-1, "USD").IsFailure);
        Assert.True(Money.Create(1, "US").IsFailure);
    }
}

public sealed class PermissionsProjectionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Role = Guid.NewGuid();

    [Fact]
    public void ApplyIfNewer_and_union()
    {
        var p = UserPermissionsProjection.Create(Tenant, User, 1, ["old"], [Role]);
        p.ApplyIfNewer(2, ["inventory.read"], [Role]);
        Assert.Contains("inventory.read", p.PermissionCodes());
        p.ApplyIfNewer(1, ["stale"], [Role]);
        Assert.DoesNotContain("stale", p.PermissionCodes());
        p.ReapplyPermissionsUnion(["inventory.read", "inventory.adjust"]);
        Assert.Equal(2, p.PermissionsVersion);
        Assert.Contains("inventory.adjust", p.PermissionCodes());
    }
}
