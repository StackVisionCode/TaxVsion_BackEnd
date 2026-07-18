using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.ValueObjects;

public sealed class LayoutKeyTests
{
    [Theory]
    [InlineData("system-base")]
    [InlineData("tenant-base")]
    [InlineData("System-Base-V2")]
    public void Create_normalizes_and_accepts_valid_keys(string candidate)
    {
        var result = LayoutKey.Create(candidate);

        Assert.True(result.IsSuccess);
        Assert.Equal(candidate.Trim().ToLowerInvariant(), result.Value.Value);
    }

    [Fact]
    public void Create_rejects_empty_key()
    {
        var result = LayoutKey.Create(null);

        Assert.True(result.IsFailure);
        Assert.Equal("LayoutKey.Empty", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_malformed_key()
    {
        var result = LayoutKey.Create("not a valid key!");

        Assert.True(result.IsFailure);
        Assert.Equal("LayoutKey.Format", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_key_longer_than_max_length()
    {
        var tooLong = new string('a', LayoutKey.MaxLength + 1);

        var result = LayoutKey.Create(tooLong);

        Assert.True(result.IsFailure);
        Assert.Equal("LayoutKey.Length", result.Error.Code);
    }
}
