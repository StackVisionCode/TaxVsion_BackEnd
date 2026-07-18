using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.ValueObjects;

public sealed class TemplateKeyTests
{
    [Theory]
    [InlineData("auth.password_reset")]
    [InlineData("sig.verification-challenge.v1")]
    [InlineData("Auth.Welcome")]
    public void Create_normalizes_and_accepts_valid_keys(string candidate)
    {
        var result = TemplateKey.Create(candidate);

        Assert.True(result.IsSuccess);
        Assert.Equal(candidate.Trim().ToLowerInvariant(), result.Value.Value);
    }

    [Fact]
    public void Create_rejects_empty_key()
    {
        var result = TemplateKey.Create("  ");

        Assert.True(result.IsFailure);
        Assert.Equal("TemplateKey.Empty", result.Error.Code);
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("Trailing.")]
    [InlineData(".leading")]
    [InlineData("double..dot")]
    public void Create_rejects_malformed_keys(string candidate)
    {
        var result = TemplateKey.Create(candidate);

        Assert.True(result.IsFailure);
        Assert.Equal("TemplateKey.Format", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_key_longer_than_max_length()
    {
        var tooLong = new string('a', TemplateKey.MaxLength + 1);

        var result = TemplateKey.Create(tooLong);

        Assert.True(result.IsFailure);
        Assert.Equal("TemplateKey.Length", result.Error.Code);
    }
}
