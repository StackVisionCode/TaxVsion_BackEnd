using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

public sealed class ProviderCustomerReferenceTests
{
    [Fact]
    public void Create_with_an_empty_value_fails()
    {
        var result = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "  ");

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderCustomerReference.Empty", result.Error.Code);
    }

    [Fact]
    public void Two_references_with_the_same_provider_and_value_are_equal()
    {
        var a = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "cus_123").Value;
        var b = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "cus_123").Value;

        Assert.Equal(a, b);
    }

    [Fact]
    public void References_with_different_providers_are_not_equal_even_with_the_same_value()
    {
        var stripe = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "abc123").Value;
        var intellipay = ProviderCustomerReference.Create(PaymentProviderCode.Intellipay, "abc123").Value;

        Assert.NotEqual(stripe, intellipay);
    }
}
