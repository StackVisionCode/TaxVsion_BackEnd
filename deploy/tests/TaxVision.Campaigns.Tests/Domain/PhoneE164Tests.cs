using TaxVision.Campaigns.Domain;

namespace TaxVision.Campaigns.Tests.Domain;

public sealed class PhoneE164Tests
{
    [Fact]
    public void Create_PresentationFormat_ReturnsE164() =>
        Assert.Equal("+18295924420", PhoneE164.Create("+1 (829) 592-4420").Value.Value);

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("8295924420")]
    [InlineData("+123")]
    [InlineData("+0829592442")]
    [InlineData("+1234567890123456")]
    public void Create_Invalid_Fails(string? raw) => Assert.True(PhoneE164.Create(raw).IsFailure);

    [Fact]
    public void Normalize_Invalid_ReturnsNull() => Assert.Null(PhoneE164.Normalize("829-592-4420"));
}
