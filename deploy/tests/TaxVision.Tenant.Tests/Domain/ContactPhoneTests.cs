using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class ContactPhoneTests
{
    [Theory]
    [InlineData("5551234567", "(555) 123-4567")]
    [InlineData("+1 (555) 123-4567", "(555) 123-4567")]
    [InlineData("1-555-123-4567", "(555) 123-4567")]
    [InlineData("+44 20 7946 0958", "+442079460958")]
    public void Normalizes_us_numbers_and_keeps_international_ones(string raw, string expected)
    {
        var result = ContactPhone.CreateOptional(raw, "Tenant.Profile.Phone");

        Assert.Equal(expected, result.Value!.Value);
    }

    [Theory]
    [InlineData("555-1234")]
    [InlineData("555 123 4567 ext 9")]
    [InlineData("1234567890123456")]
    public void Rejects_with_the_code_of_the_field_that_uses_it(string raw)
    {
        var result = ContactPhone.CreateOptional(raw, "Tenant.Compliance.IncidentContactPhone");

        Assert.Equal("Tenant.Compliance.IncidentContactPhone", result.Error.Code);
    }

    [Fact]
    public void Blank_means_no_phone()
    {
        Assert.Null(ContactPhone.CreateOptional("  ", "Tenant.Profile.Phone").Value);
    }
}
