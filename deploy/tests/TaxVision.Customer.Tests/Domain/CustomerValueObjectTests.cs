using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Tests.Domain;

public sealed class CustomerValueObjectTests
{
    [Fact]
    public void Email_preserves_display_value_and_normalizes_comparison_value()
    {
        var result = EmailAddress.Create("  User.Name@Example.COM ");

        Assert.True(result.IsSuccess);
        Assert.Equal("User.Name@Example.COM", result.Value.Value);
        Assert.Equal("user.name@example.com", result.Value.NormalizedValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing-at.example.com")]
    [InlineData("@example.com")]
    public void Email_rejects_invalid_values(string raw)
    {
        Assert.True(EmailAddress.Create(raw).IsFailure);
    }

    [Fact]
    public void Phone_normalizes_common_format_to_e164()
    {
        var result = PhoneNumber.Create("+1 (305) 555-0123");

        Assert.True(result.IsSuccess);
        Assert.Equal("+13055550123", result.Value.E164Value);
    }

    // Sin código: 10 dígitos (o 11 empezando por 1) son de +1. Con código, cualquier país.
    [Theory]
    [InlineData("8095551234", "+18095551234")]
    [InlineData("(809) 555-1234", "+18095551234")]
    [InlineData("1 809 555 1234", "+18095551234")]
    [InlineData("+52 55 1234 5678", "+525512345678")]
    public void Phone_without_country_code_defaults_to_plus_one(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Create(raw).Value.E164Value);
    }

    [Theory]
    [InlineData("5551234")]
    [InlineData("28095551234")]
    [InlineData("+0123456789")]
    [InlineData("+1 809 555")]
    [InlineData("+1809555123")]
    public void Phone_rejects_numbers_that_are_neither_e164_nor_ten_digits(string raw)
    {
        Assert.True(PhoneNumber.Create(raw).IsFailure);
    }

    [Fact]
    public void Personal_name_builds_display_name_without_empty_segments()
    {
        var result = PersonalName.Create("Jane", "Doe", middleName: "A.");

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane A. Doe", result.Value.DisplayName);
    }
}
