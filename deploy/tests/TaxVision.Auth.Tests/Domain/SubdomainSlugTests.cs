using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>
/// Fase A2 — validación de formato (etiqueta DNS) y de la blocklist de subdominios
/// reservados. La unicidad real la garantiza el índice único de BD (fuera de alcance
/// de un test de dominio puro).
/// </summary>
public sealed class SubdomainSlugTests
{
    [Theory]
    [InlineData("oficina1")]
    [InlineData("mi-oficina")]
    [InlineData("abc")]
    [InlineData("a1b2c3")]
    public void Valid_slugs_are_accepted(string slug)
    {
        var result = SubdomainSlug.Create(slug);

        Assert.True(result.IsSuccess);
        Assert.Equal(slug, result.Value.Value);
    }

    [Fact]
    public void Slug_is_normalized_to_lowercase_and_trimmed()
    {
        var result = SubdomainSlug.Create("  MiOficina  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("mioficina", result.Value.Value);
    }

    [Theory]
    [InlineData("ab")] // muy corto (&lt;3)
    [InlineData("")]
    [InlineData(null)]
    public void Too_short_slugs_are_rejected(string? slug)
    {
        var result = SubdomainSlug.Create(slug);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugLength", result.Error.Code);
    }

    [Fact]
    public void Too_long_slug_is_rejected()
    {
        var result = SubdomainSlug.Create(new string('a', 64));

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugLength", result.Error.Code);
    }

    [Theory]
    [InlineData("-oficina")] // guion inicial
    [InlineData("oficina-")] // guion final
    [InlineData("mi_oficina")] // guion bajo no permitido
    [InlineData("oficina uno")] // espacio
    [InlineData("oficina.uno")] // punto
    public void Malformed_slugs_are_rejected(string slug)
    {
        var result = SubdomainSlug.Create(slug);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugInvalid", result.Error.Code);
    }

    [Fact]
    public void Punycode_prefixed_slug_is_rejected()
    {
        var result = SubdomainSlug.Create("xn--ofic-1na");

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugInvalid", result.Error.Code);
    }

    [Theory]
    [InlineData("www")]
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("billing")]
    [InlineData("staging")]
    [InlineData("turn")]
    public void Reserved_slugs_are_rejected(string slug)
    {
        var result = SubdomainSlug.Create(slug);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantDomain.SlugReserved", result.Error.Code);
    }
}
