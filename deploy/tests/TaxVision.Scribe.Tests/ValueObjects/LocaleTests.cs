using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.ValueObjects;

public sealed class LocaleTests
{
    [Theory]
    [InlineData("es-US", "es-US")]
    [InlineData("es-us", "es-US")]
    [InlineData("EN-us", "en-US")]
    [InlineData("es", "es")]
    public void Create_normalizes_language_lowercase_and_region_uppercase(string candidate, string expected)
    {
        var result = Locale.Create(candidate);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Fact]
    public void Create_rejects_empty_locale()
    {
        var result = Locale.Create(" ");

        Assert.True(result.IsFailure);
        Assert.Equal("Locale.Empty", result.Error.Code);
    }

    [Theory]
    [InlineData("english")]
    [InlineData("es-USA")]
    [InlineData("e")]
    public void Create_rejects_malformed_locale(string candidate)
    {
        var result = Locale.Create(candidate);

        Assert.True(result.IsFailure);
        Assert.Equal("Locale.Format", result.Error.Code);
    }
}
