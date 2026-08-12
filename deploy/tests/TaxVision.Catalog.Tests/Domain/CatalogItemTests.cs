using TaxVision.Catalog.Domain;
using TaxVision.Catalog.Domain.Items;
using TaxVision.Catalog.Domain.ValueObjects;

namespace TaxVision.Catalog.Tests.Domain;

public sealed class CatalogItemTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Category = Guid.NewGuid();
    private static Money Price => Money.Create(100, "USD").Value;

    private static CatalogItem NewProduct(string? sku = "SKU-1", ItemKind kind = ItemKind.Product, bool track = true) =>
        CatalogItem.Create(Tenant, User, "Widget", "desc", sku, "BC-1", Category, kind, Price, null, "unit", track, null, Now).Value;

    [Fact]
    public void Create_snapshots_fields_and_starts_active()
    {
        var item = NewProduct();

        Assert.Equal(Tenant, item.TenantId);
        Assert.Equal("Widget", item.Name);
        Assert.Equal("SKU-1", item.Sku);
        Assert.True(item.IsActive);
        Assert.False(item.IsDeleted);
        Assert.Equal("USD", item.Price.Currency);
    }

    [Fact]
    public void Create_uppercases_and_trims_sku()
    {
        var item = CatalogItem.Create(Tenant, User, "W", null, "  abc-9 ", null, Category, ItemKind.Product, Price, null, null, true, null, Now).Value;
        Assert.Equal("ABC-9", item.Sku);
    }

    [Fact]
    public void Service_never_tracks_inventory_even_if_requested()
    {
        var svc = NewProduct(kind: ItemKind.Service, track: true);
        Assert.False(svc.TrackInventory);
    }

    [Fact]
    public void Product_honors_track_inventory_flag()
    {
        Assert.True(NewProduct(track: true).TrackInventory);
        Assert.False(NewProduct(track: false).TrackInventory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string name)
    {
        var r = CatalogItem.Create(Tenant, User, name, null, null, null, Category, ItemKind.Product, Price, null, null, true, null, Now);
        Assert.True(r.IsFailure);
        Assert.Equal(CatalogErrors.InvalidName.Code, r.Error.Code);
    }

    [Fact]
    public void Create_rejects_empty_tenant_and_category()
    {
        Assert.Equal(CatalogErrors.InvalidTenant.Code,
            CatalogItem.Create(Guid.Empty, User, "W", null, null, null, Category, ItemKind.Product, Price, null, null, true, null, Now).Error.Code);
        Assert.Equal(CatalogErrors.InvalidCategory.Code,
            CatalogItem.Create(Tenant, User, "W", null, null, null, Guid.Empty, ItemKind.Product, Price, null, null, true, null, Now).Error.Code);
    }

    [Fact]
    public void Create_rejects_sku_over_max_length()
    {
        var longSku = new string('a', CatalogItem.SkuMax + 1);
        var r = CatalogItem.Create(Tenant, User, "W", null, longSku, null, Category, ItemKind.Product, Price, null, null, true, null, Now);
        Assert.True(r.IsFailure);
        Assert.Equal(CatalogErrors.InvalidSku.Code, r.Error.Code);
    }

    [Fact]
    public void ChangePrice_updates_price_and_cost()
    {
        var item = NewProduct();
        item.ChangePrice(Money.Create(250, "DOP").Value, Money.Create(90, "DOP").Value, Now);
        Assert.Equal(250, item.Price.Amount);
        Assert.Equal("DOP", item.Price.Currency);
        Assert.Equal(90, item.Cost!.Amount);
    }

    [Fact]
    public void SoftDelete_marks_deleted_idempotently()
    {
        var item = NewProduct();
        item.SoftDelete(Now);
        Assert.True(item.IsDeleted);
        Assert.NotNull(item.DeletedAtUtc);
        item.SoftDelete(Now.AddDays(1)); // idempotent
        Assert.True(item.IsDeleted);
    }

    [Fact]
    public void ReplaceAttributes_sets_and_skips_blank()
    {
        var item = NewProduct();
        item.ReplaceAttributes([("color", "red", "string"), ("", "x", null), ("size", "L", null)]);
        Assert.Equal(2, item.Attributes.Count);
        Assert.Contains(item.Attributes, a => a.Key == "color" && a.Value == "red");
    }

    [Fact]
    public void Update_changes_fields_and_validates_name()
    {
        var item = NewProduct();
        var newCat = Guid.NewGuid();
        Assert.True(item.Update("Widget v2", "d2", "BC-2", newCat, "u2", "img", Now).IsSuccess);
        Assert.Equal("Widget v2", item.Name);
        Assert.Equal(newCat, item.CategoryId);
        Assert.True(item.Update("", null, null, newCat, null, null, Now).IsFailure);
    }
}
