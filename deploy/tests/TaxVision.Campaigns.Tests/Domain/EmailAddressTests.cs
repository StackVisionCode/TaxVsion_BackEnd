using TaxVision.Campaigns.Domain;

namespace TaxVision.Campaigns.Tests.Domain;

public sealed class EmailAddressTests
{
    [Fact]
    public void Create_MixedCaseWithSpaces_Normalizes() =>
        Assert.Equal("ana@mail.com", EmailAddress.Create("  Ana@Mail.COM ").Value.Value);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@mail.com")]
    [InlineData("ana@")]
    public void Create_Invalid_Fails(string? raw) => Assert.True(EmailAddress.Create(raw).IsFailure);

    [Fact]
    public void Create_LongerThanTheColumn_Fails() =>
        Assert.True(EmailAddress.Create(new string('a', EmailAddress.MaxLength) + "@mail.com").IsFailure);

    [Fact]
    public void Normalize_Invalid_ReturnsNull() => Assert.Null(EmailAddress.Normalize("no-at-sign"));

    [Fact]
    public void Normalize_Valid_ReturnsTheStoredForm() =>
        Assert.Equal("ana@mail.com", EmailAddress.Normalize(" Ana@Mail.COM"));
}
