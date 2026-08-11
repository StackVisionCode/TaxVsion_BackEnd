using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.ValueObjects;

namespace TaxVision.Sms.Tests.Domain;

public sealed class SmsBodyTests
{
    [Fact]
    public void Create_trims_and_accepts_non_empty_body()
    {
        var result = SmsBody.Create("  hello world  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("hello world", result.Value.Value);
    }

    [Fact]
    public void Create_accepts_body_at_max_length()
    {
        var result = SmsBody.Create(new string('a', SmsBody.MaxLength));

        Assert.True(result.IsSuccess);
        Assert.Equal(SmsBody.MaxLength, result.Value.Value.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_empty_body(string? raw)
    {
        var result = SmsBody.Create(raw);

        Assert.True(result.IsFailure);
        Assert.Equal(SmsErrors.InvalidBody.Code, result.Error.Code);
    }

    [Fact]
    public void Create_rejects_body_over_max_length()
    {
        var result = SmsBody.Create(new string('a', SmsBody.MaxLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal(SmsErrors.InvalidBody.Code, result.Error.Code);
    }
}
