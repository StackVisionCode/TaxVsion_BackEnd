using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("user@example.com", "user@example.com")]
    [InlineData("  USER@Example.COM  ", "user@example.com")]
    [InlineData("first.last+tag@sub.example.com", "first.last+tag@sub.example.com")]
    public void Valid_address_is_normalized(string raw, string expected)
    {
        var address = EmailAddress.Create(raw);

        Assert.True(address.IsSuccess);
        Assert.Equal(expected, address.Value.Value);
        Assert.Equal(expected, address.Value.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-un-correo")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    public void Invalid_address_is_rejected(string raw) => Assert.True(EmailAddress.Create(raw).IsFailure);

    [Fact]
    public void Null_is_rejected() => Assert.True(EmailAddress.Create(null).IsFailure);

    [Fact]
    public void Address_longer_than_the_column_is_rejected()
    {
        var local = new string('a', EmailAddress.MaxLength);

        Assert.True(EmailAddress.Create($"{local}@example.com").IsFailure);
    }

    [Fact]
    public void Address_at_the_limit_is_accepted()
    {
        const string domain = "@example.com";
        var local = new string('a', EmailAddress.MaxLength - domain.Length);

        Assert.True(EmailAddress.Create(local + domain).IsSuccess);
    }

    [Fact]
    public void Same_address_with_other_casing_is_equal() =>
        Assert.Equal(EmailAddress.Create("A@B.com").Value, EmailAddress.Create(" a@b.COM ").Value);
}
