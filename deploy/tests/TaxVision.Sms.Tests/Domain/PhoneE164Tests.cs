using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.ValueObjects;

namespace TaxVision.Sms.Tests.Domain;

public sealed class PhoneE164Tests
{
    [Theory]
    [InlineData("+18095551234", "+18095551234")]
    [InlineData("+1 809 555 1234", "+18095551234")]
    [InlineData("+1 (809) 555-1234", "+18095551234")]
    [InlineData(" +1-809-555.1234 ", "+18095551234")]
    public void Create_normalizes_and_accepts_valid_e164(string raw, string expected)
    {
        var result = PhoneE164.Create(raw);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("8095551234")] // no '+'
    [InlineData("+0809555")] // leading zero country code not allowed
    [InlineData("+123")] // too short (needs >= 7 digits)
    [InlineData("+1234567890123456")] // too long (> 15 digits)
    [InlineData("+1809ABC1234")] // letters
    public void Create_rejects_invalid_destinations(string? raw)
    {
        var result = PhoneE164.Create(raw);

        Assert.True(result.IsFailure);
        Assert.Equal(SmsErrors.InvalidDestination.Code, result.Error.Code);
    }
}
