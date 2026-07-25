using TaxVision.Tenant.Domain;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class TenantBrandingTests
{
    private static TaxVision.Tenant.Domain.Tenant NewTenant() =>
        TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York").Value;

    [Fact]
    public void SetBrandingColors_with_valid_hex_values_succeeds()
    {
        var tenant = NewTenant();

        var result = tenant.SetBrandingColors("#112233", "#AABBCC", "#FFFFFF", "#000000");

        Assert.True(result.IsSuccess);
        Assert.Equal("#112233", tenant.PrimaryColor!.Value);
        Assert.Equal("#AABBCC", tenant.AccentColor!.Value);
        Assert.Equal("#FFFFFF", tenant.BackgroundColor!.Value);
        Assert.Equal("#000000", tenant.TextColor!.Value);
    }

    [Fact]
    public void SetBrandingColors_normalizes_lowercase_hex_to_uppercase()
    {
        var tenant = NewTenant();

        tenant.SetBrandingColors("#1e466b", null, null, null);

        Assert.Equal("#1E466B", tenant.PrimaryColor!.Value);
    }

    [Fact]
    public void SetBrandingColors_leaves_field_as_default_when_null()
    {
        var tenant = NewTenant();

        var result = tenant.SetBrandingColors("#112233", null, null, null);

        Assert.True(result.IsSuccess);
        Assert.NotNull(tenant.PrimaryColor);
        Assert.Null(tenant.AccentColor);
        Assert.Null(tenant.BackgroundColor);
        Assert.Null(tenant.TextColor);
    }

    [Theory]
    [InlineData("azul")]
    [InlineData("#FFF")]
    [InlineData("112233")]
    [InlineData("#GGGGGG")]
    [InlineData("#11223")]
    public void SetBrandingColors_rejects_invalid_hex_format(string invalidHex)
    {
        var tenant = NewTenant();

        var result = tenant.SetBrandingColors(invalidHex, null, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("HexColor.InvalidFormat", result.Error.Code);
    }

    [Fact]
    public void SetBrandingColors_is_atomic_and_applies_nothing_when_one_field_is_invalid()
    {
        var tenant = NewTenant();
        tenant.SetBrandingColors("#112233", "#AABBCC", "#FFFFFF", "#000000");

        var result = tenant.SetBrandingColors("#654321", "not-a-color", "#FFFFFF", "#000000");

        Assert.True(result.IsFailure);
        // Ningun campo se pisa si un solo valor del patch es invalido — evita dejar la paleta a medias.
        Assert.Equal("#112233", tenant.PrimaryColor!.Value);
    }

    [Fact]
    public void ResetBrandingColors_clears_all_fields()
    {
        var tenant = NewTenant();
        tenant.SetBrandingColors("#112233", "#AABBCC", "#FFFFFF", "#000000");

        tenant.ResetBrandingColors();

        Assert.Null(tenant.PrimaryColor);
        Assert.Null(tenant.AccentColor);
        Assert.Null(tenant.BackgroundColor);
        Assert.Null(tenant.TextColor);
    }

    [Fact]
    public void ResetBrandingColors_is_idempotent_when_never_customized()
    {
        var tenant = NewTenant();

        tenant.ResetBrandingColors();

        Assert.Null(tenant.PrimaryColor);
    }

    [Fact]
    public void ResolveBrandingPalette_falls_back_to_system_defaults_when_never_customized()
    {
        var tenant = NewTenant();

        var palette = tenant.ResolveBrandingPalette();

        Assert.Equal(SystemBrandingDefaults.PrimaryColor, palette.PrimaryColor);
        Assert.Equal(SystemBrandingDefaults.AccentColor, palette.AccentColor);
        Assert.Equal(SystemBrandingDefaults.BackgroundColor, palette.BackgroundColor);
        Assert.Equal(SystemBrandingDefaults.TextColor, palette.TextColor);
        Assert.False(palette.IsCustomized);
    }

    [Fact]
    public void ResolveBrandingPalette_mixes_custom_and_default_fields_independently()
    {
        var tenant = NewTenant();
        tenant.SetBrandingColors("#112233", null, null, null);

        var palette = tenant.ResolveBrandingPalette();

        Assert.Equal("#112233", palette.PrimaryColor.Value);
        Assert.Equal(SystemBrandingDefaults.AccentColor, palette.AccentColor);
        Assert.True(palette.IsCustomized);
    }

    [Fact]
    public void ResolveBrandingPalette_reports_not_customized_after_reset()
    {
        var tenant = NewTenant();
        tenant.SetBrandingColors("#112233", "#AABBCC", "#FFFFFF", "#000000");

        tenant.ResetBrandingColors();
        var palette = tenant.ResolveBrandingPalette();

        Assert.False(palette.IsCustomized);
        Assert.Equal(SystemBrandingDefaults.PrimaryColor, palette.PrimaryColor);
    }
}
