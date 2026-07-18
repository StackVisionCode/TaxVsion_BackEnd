using TaxVision.PaymentClient.Domain.PaymentLinks;

namespace TaxVision.PaymentClient.Tests.Domain;

public sealed class PaymentLinkTokenTests
{
    [Fact]
    public void Generate_produces_a_URL_safe_value_with_no_padding()
    {
        var token = PaymentLinkToken.Generate();

        Assert.DoesNotContain('+', token.Value);
        Assert.DoesNotContain('/', token.Value);
        Assert.DoesNotContain('=', token.Value);
    }

    [Fact]
    public void Generate_produces_distinct_tokens()
    {
        var a = PaymentLinkToken.Generate();
        var b = PaymentLinkToken.Generate();

        Assert.NotEqual(a.Value, b.Value);
    }

    [Fact]
    public void FromExisting_with_an_empty_value_fails()
    {
        var result = PaymentLinkToken.FromExisting("   ");

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentLinkToken.Empty", result.Error.Code);
    }

    [Fact]
    public void FromExisting_round_trips_a_generated_value()
    {
        var generated = PaymentLinkToken.Generate();

        var reconstructed = PaymentLinkToken.FromExisting(generated.Value);

        Assert.True(reconstructed.IsSuccess);
        Assert.Equal(generated.Value, reconstructed.Value.Value);
    }
}
