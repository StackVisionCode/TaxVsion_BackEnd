using System.Reflection;
using TaxVision.Connectors.Domain.Accounts;

namespace TaxVision.Connectors.Tests.Domain;

public sealed class MailboxAddressTests
{
    [Fact]
    public void Create_MixedCase_NormalizesOnce() =>
        Assert.Equal("ana@mail.com", MailboxAddress.Create("  Ana@Mail.COM ").Value.Value);

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("no-at-sign")]
    [InlineData("@mail.com")]
    [InlineData("ana@")]
    public void Create_Invalid_KeepsTheAccountErrorCode(string? raw)
    {
        var result = MailboxAddress.Create(raw);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.EmailAddress", result.Error.Code);
    }

    [Fact]
    public void Create_LongerThanTheColumn_Fails() =>
        Assert.True(MailboxAddress.Create(new string('a', MailboxAddress.MaxLength) + "@mail.com").IsFailure);

    [Theory]
    [InlineData(" Ana@Mail.COM ", true)]
    [InlineData("ana@mail.com", true)]
    [InlineData("other@mail.com", false)]
    [InlineData(null, false)]
    public void Matches_ComparesWithTheSameRule(string? raw, bool expected) =>
        Assert.Equal(expected, MailboxAddress.Create("ana@mail.com").Value.Matches(raw));

    // CON-14: la regla de normalización no sale del value object; el resto compara con él.
    [Fact]
    public void Normalization_IsNotExposedAsAStringHelper() =>
        Assert.Null(typeof(MailboxAddress).GetMethod("Normalize", BindingFlags.Public | BindingFlags.Static));
}
