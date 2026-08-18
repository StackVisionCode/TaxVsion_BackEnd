using TaxVision.Documents.Domain.Branding;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

public sealed class DocumentBrandingTests
{
    private static readonly DateTime Now = new(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_trims_and_stores_fields()
    {
        var result = DocumentBranding.Create(
            Guid.NewGuid(),
            "  ACME  ",
            "data:image/png;base64,AAAA",
            "#8b1e3f",
            "  pie  ",
            Now
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("ACME", result.Value.DisplayName);
        Assert.Equal("#8b1e3f", result.Value.BrandColorHex);
        Assert.Equal("pie", result.Value.FooterText);
    }

    [Fact]
    public void Create_rejects_a_non_hex_color()
    {
        var result = DocumentBranding.Create(Guid.NewGuid(), "ACME", null, "rojo", null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Documents.Branding.InvalidColor", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_a_logo_that_is_not_an_embedded_data_uri()
    {
        var result = DocumentBranding.Create(
            Guid.NewGuid(),
            "ACME",
            "https://cdn.example.com/logo.png",
            null,
            null,
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Documents.Branding.InvalidLogo", result.Error.Code);
    }

    [Fact]
    public void Update_replaces_fields_and_validates()
    {
        var branding = DocumentBranding.Create(Guid.NewGuid(), "ACME", null, "#000000", null, Now).Value;

        var ok = branding.Update("ACME 2", "data:image/svg+xml;base64,BBBB", "#fff", "gracias", Now.AddDays(1));
        Assert.True(ok.IsSuccess);
        Assert.Equal("ACME 2", branding.DisplayName);
        Assert.Equal("#fff", branding.BrandColorHex);

        var bad = branding.Update(null, null, "not-a-color", null, Now.AddDays(2));
        Assert.True(bad.IsFailure);
    }
}
