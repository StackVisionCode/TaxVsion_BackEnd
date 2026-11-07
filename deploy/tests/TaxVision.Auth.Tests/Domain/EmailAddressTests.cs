using TaxVision.Auth.Domain.Common;

namespace TaxVision.Auth.Tests.Domain;

public sealed class EmailAddressTests
{
    [Fact]
    public void Create_NormalizesCaseAndTrims()
    {
        var email = EmailAddress.Create(" Foo@Bar.COM ");

        Assert.True(email.IsSuccess);
        Assert.Equal("foo@bar.com", email.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("foo")]
    [InlineData("foo@")]
    [InlineData("@bar.com")]
    public void Create_InvalidFormat_ReturnsFailure(string? raw)
    {
        var email = EmailAddress.Create(raw);

        Assert.True(email.IsFailure);
    }

    // Lo que ya está en la base no se revalida: una fila vieja no puede romper la lectura.
    [Fact]
    public void FromTrusted_KeepsTheStoredValueAsIs()
    {
        Assert.Equal("legacy@", EmailAddress.FromTrusted("legacy@").Value);
    }

    [Fact]
    public void ComparesByValue_AndReadsAsItsText()
    {
        var email = EmailAddress.Create("foo@bar.com").Value;

        Assert.Equal(EmailAddress.Create("FOO@bar.com").Value, email);
        Assert.Equal("foo@bar.com", email.ToString());
        string text = email;
        Assert.Equal("foo@bar.com", text);
    }
}
