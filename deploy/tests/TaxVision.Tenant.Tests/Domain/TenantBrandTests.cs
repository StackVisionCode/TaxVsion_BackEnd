using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class TenantBrandTests
{
    private static TenantBrand NewBrand() => TenantBrand.Create(Guid.NewGuid(), BrandSurface.Crm, DateTime.UtcNow);

    // ----- Creación -----

    [Fact]
    public void Create_sets_tenant_surface_and_starts_empty()
    {
        var tenantId = Guid.NewGuid();

        var brand = TenantBrand.Create(tenantId, BrandSurface.Portal, DateTime.UtcNow);

        Assert.Equal(tenantId, brand.TenantId);
        Assert.Equal(BrandSurface.Portal, brand.Surface);
        Assert.Empty(brand.Colors);
        Assert.Empty(brand.Assets);
    }

    // ----- Tiempo -----

    [Fact]
    public void SetAsset_UsesProvidedTimestamp()
    {
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var uploaded = created.AddHours(1);
        var brand = TenantBrand.Create(Guid.NewGuid(), BrandSurface.Crm, created);

        var result = brand.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, 10, 10, uploaded);

        Assert.True(result.IsSuccess);
        Assert.Equal(created, brand.CreatedAtUtc);
        Assert.Equal(uploaded, brand.UpdatedAtUtc);
        Assert.Equal(uploaded, Assert.Single(brand.Assets).UpdatedAtUtc);
    }

    // ----- Colores -----

    [Fact]
    public void SetColor_adds_a_color()
    {
        var brand = NewBrand();

        var result = brand.SetColor(BrandColorToken.Primary, "#1E466B", DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        var color = Assert.Single(brand.Colors);
        Assert.Equal(BrandColorToken.Primary, color.Token);
        Assert.Equal("#1E466B", color.Color.Value);
    }

    [Fact]
    public void SetColor_twice_on_same_token_updates_in_place_not_duplicates()
    {
        var brand = NewBrand();

        brand.SetColor(BrandColorToken.Primary, "#1E466B", DateTime.UtcNow);
        brand.SetColor(BrandColorToken.Primary, "#0F5132", DateTime.UtcNow);

        var color = Assert.Single(brand.Colors);
        Assert.Equal("#0F5132", color.Color.Value);
    }

    [Fact]
    public void SetColor_different_tokens_coexist()
    {
        var brand = NewBrand();

        brand.SetColor(BrandColorToken.Primary, "#1E466B", DateTime.UtcNow);
        brand.SetColor(BrandColorToken.Accent, "#67BAF4", DateTime.UtcNow);

        Assert.Equal(2, brand.Colors.Count);
    }

    [Fact]
    public void SetColor_with_invalid_hex_fails_and_adds_nothing()
    {
        var brand = NewBrand();

        var result = brand.SetColor(BrandColorToken.Primary, "not-a-color", DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Empty(brand.Colors);
    }

    [Fact]
    public void RemoveColor_removes_when_present_and_is_idempotent_when_absent()
    {
        var brand = NewBrand();
        brand.SetColor(BrandColorToken.Primary, "#1E466B", DateTime.UtcNow);

        brand.RemoveColor(BrandColorToken.Primary, DateTime.UtcNow);
        brand.RemoveColor(BrandColorToken.Primary, DateTime.UtcNow); // no debe lanzar

        Assert.Empty(brand.Colors);
    }

    [Fact]
    public void ResetColors_clears_everything()
    {
        var brand = NewBrand();
        brand.SetColor(BrandColorToken.Primary, "#1E466B", DateTime.UtcNow);
        brand.SetColor(BrandColorToken.Accent, "#67BAF4", DateTime.UtcNow);

        brand.ResetColors(DateTime.UtcNow);

        Assert.Empty(brand.Colors);
    }

    // ----- Assets: invariantes -----

    [Fact]
    public void SetAssetPending_with_valid_png_creates_a_pending_asset()
    {
        var brand = NewBrand();
        var fileId = Guid.NewGuid();

        var result = brand.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100_000, 200, 60, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        var asset = Assert.Single(brand.Assets);
        Assert.Equal(BrandAssetKey.Logo, asset.Key);
        Assert.Equal(fileId, asset.FileId);
        Assert.Equal(BrandAssetStatus.Pending, asset.Status);
        Assert.Null(asset.ConfirmedAtUtc);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/gif")]
    [InlineData("")]
    public void SetAssetPending_rejects_disallowed_content_type(string contentType)
    {
        var brand = NewBrand();

        var result = brand.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), contentType, 100_000, null, null, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Empty(brand.Assets);
    }

    [Fact]
    public void SetAssetPending_rejects_oversize()
    {
        var brand = NewBrand();

        var result = brand.SetAssetPending(
            BrandAssetKey.Logo,
            Guid.NewGuid(),
            "image/png",
            TenantBrand.MaxAssetSizeBytes + 1,
            null,
            null,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Empty(brand.Assets);
    }

    [Fact]
    public void SetAssetPending_rejects_empty_file_id_and_zero_size()
    {
        var brand = NewBrand();

        Assert.True(brand.SetAssetPending(BrandAssetKey.Logo, Guid.Empty, "image/png", 100, null, null, DateTime.UtcNow).IsFailure);
        Assert.True(brand.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 0, null, null, DateTime.UtcNow).IsFailure);
        Assert.Empty(brand.Assets);
    }

    [Fact]
    public void Logo_and_favicon_coexist_as_independent_assets()
    {
        var brand = NewBrand();

        brand.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null, DateTime.UtcNow);
        brand.SetAssetPending(BrandAssetKey.Favicon, Guid.NewGuid(), "image/svg+xml", 100, null, null, DateTime.UtcNow);

        Assert.Equal(2, brand.Assets.Count);
    }

    // ----- Assets: ciclo de vida pending → confirmed -----

    [Fact]
    public void ConfirmAsset_with_matching_file_id_confirms_it()
    {
        var brand = NewBrand();
        var fileId = Guid.NewGuid();
        var confirmedAt = DateTime.UtcNow;
        brand.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100_000, 200, 60, DateTime.UtcNow);

        var result = brand.ConfirmAsset(BrandAssetKey.Logo, fileId, "image/png", 98_000, 200, 60, confirmedAt);

        Assert.True(result.IsSuccess);
        var asset = Assert.Single(brand.Assets);
        Assert.Equal(BrandAssetStatus.Confirmed, asset.Status);
        Assert.Equal(confirmedAt, asset.ConfirmedAtUtc);
        Assert.Equal(98_000, asset.SizeBytes); // tomó los metadatos reales del escaneo
    }

    [Fact]
    public void ConfirmAsset_for_a_superseded_file_id_is_ignored()
    {
        var brand = NewBrand();
        var replacedFileId = Guid.NewGuid();
        var currentFileId = Guid.NewGuid();
        brand.SetAssetPending(BrandAssetKey.Logo, replacedFileId, "image/png", 100, null, null, DateTime.UtcNow);
        brand.SetAssetPending(BrandAssetKey.Logo, currentFileId, "image/png", 200, null, null, DateTime.UtcNow); // reemplazo

        // El escaneo del archivo VIEJO llega tarde: no debe confirmar el nuevo.
        var result = brand.ConfirmAsset(
            BrandAssetKey.Logo,
            replacedFileId,
            "image/png",
            100,
            null,
            null,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        var asset = Assert.Single(brand.Assets);
        Assert.Equal(currentFileId, asset.FileId);
        Assert.Equal(BrandAssetStatus.Pending, asset.Status);
    }

    [Fact]
    public void ConfirmAsset_when_none_exists_is_a_noop_success()
    {
        var brand = NewBrand();

        var result = brand.ConfirmAsset(
            BrandAssetKey.Logo,
            Guid.NewGuid(),
            "image/png",
            100,
            null,
            null,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(brand.Assets);
    }

    [Fact]
    public void DiscardPendingAsset_removes_matching_pending_but_not_a_confirmed_one()
    {
        var brand = NewBrand();
        var fileId = Guid.NewGuid();
        brand.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow);

        // Descarta el pendiente (rechazado por antivirus).
        brand.DiscardPendingAsset(BrandAssetKey.Logo, fileId, DateTime.UtcNow);
        Assert.Empty(brand.Assets);

        // Uno ya confirmado no se descarta por un rechazo tardío.
        var confirmedFileId = Guid.NewGuid();
        brand.SetAssetPending(BrandAssetKey.Favicon, confirmedFileId, "image/png", 100, null, null, DateTime.UtcNow);
        brand.ConfirmAsset(BrandAssetKey.Favicon, confirmedFileId, "image/png", 100, null, null, DateTime.UtcNow);
        brand.DiscardPendingAsset(BrandAssetKey.Favicon, confirmedFileId, DateTime.UtcNow);
        Assert.Single(brand.Assets);
    }

    [Fact]
    public void RemoveAsset_removes_when_present_and_is_idempotent_when_absent()
    {
        var brand = NewBrand();
        brand.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null, DateTime.UtcNow);

        brand.RemoveAsset(BrandAssetKey.Logo, DateTime.UtcNow);
        brand.RemoveAsset(BrandAssetKey.Logo, DateTime.UtcNow); // no debe lanzar

        Assert.Empty(brand.Assets);
    }

    [Fact]
    public void RevokeAsset_ConfirmedWithSameFileId_RemovesAsset()
    {
        var brand = NewBrand();
        var fileId = Guid.NewGuid();
        brand.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow);
        brand.ConfirmAsset(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow);

        Assert.True(brand.RevokeAsset(BrandAssetKey.Logo, fileId, DateTime.UtcNow));
        Assert.Empty(brand.Assets);
    }

    [Fact]
    public void RevokeAsset_WithDifferentFileId_KeepsAsset()
    {
        var brand = NewBrand();
        brand.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null, DateTime.UtcNow);

        Assert.False(brand.RevokeAsset(BrandAssetKey.Logo, Guid.NewGuid(), DateTime.UtcNow));
        Assert.Single(brand.Assets);
    }
}
