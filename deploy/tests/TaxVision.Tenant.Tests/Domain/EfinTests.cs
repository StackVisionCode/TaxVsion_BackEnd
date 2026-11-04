using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class EfinTests
{
    [Fact]
    public void Accepts_six_digits_and_masks_all_but_the_last_two()
    {
        var efin = Efin.Create(" 123456 ").Value;

        Assert.Equal("123456", efin.Value);
        Assert.Equal("56", efin.LastDigits);
        Assert.Equal("••••56", efin.ToString());
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12-456")]
    [InlineData("abcdef")]
    [InlineData(null)]
    public void Rejects_anything_that_is_not_six_digits(string? raw)
    {
        Assert.Equal(Efin.Invalid, Efin.Create(raw).Error);
    }
}
