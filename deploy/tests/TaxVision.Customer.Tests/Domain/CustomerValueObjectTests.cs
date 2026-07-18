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

    [Fact]
    public void Personal_name_builds_display_name_without_empty_segments()
    {
        var result = PersonalName.Create("Jane", "Doe", middleName: "A.");

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane A. Doe", result.Value.DisplayName);
    }
}
