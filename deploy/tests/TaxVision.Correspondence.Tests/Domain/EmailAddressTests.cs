using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("  John.Doe@Example.com  ", "john.doe@example.com")]
    [InlineData("ALLCAPS@EXAMPLE.COM", "allcaps@example.com")]
    public void Create_normalizes_via_trim_and_lowercase(string raw, string expectedNormalized)
    {
        var result = EmailAddress.Create(raw);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedNormalized, result.Value.NormalizedValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public void Create_rejects_invalid_input(string raw)
    {
        var result = EmailAddress.Create(raw);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_rejects_values_longer_than_the_max_length()
    {
        var tooLong = new string('a', EmailAddress.MaxLength) + "@example.com";

        var result = EmailAddress.Create(tooLong);

        Assert.True(result.IsFailure);
    }
}
