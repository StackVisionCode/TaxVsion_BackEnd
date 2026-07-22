using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class HexColorTests
{
    [Theory]
    [InlineData("#1E466B")]
    [InlineData("#67baf4")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    public void Create_accepts_valid_RRGGBB_format(string value)
    {
        var result = HexColor.Create(value);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_normalizes_to_uppercase()
    {
        var result = HexColor.Create("#1e466b");

        Assert.Equal("#1E466B", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_rejects_empty_or_whitespace(string? value)
    {
        var result = HexColor.Create(value!);

        Assert.True(result.IsFailure);
        Assert.Equal("HexColor.Empty", result.Error.Code);
    }

    [Theory]
    [InlineData("azul")]
    [InlineData("#FFF")]
    [InlineData("1E466B")]
    [InlineData("#GGGGGG")]
    [InlineData("#1E466")]
    [InlineData("#1E466B7")]
    public void Create_rejects_invalid_format(string value)
    {
        var result = HexColor.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal("HexColor.InvalidFormat", result.Error.Code);
    }

    [Fact]
    public void Equality_is_value_based()
    {
        var a = HexColor.Create("#1E466B").Value;
        var b = HexColor.Create("#1e466b").Value;

        Assert.Equal(a, b);
    }
}
