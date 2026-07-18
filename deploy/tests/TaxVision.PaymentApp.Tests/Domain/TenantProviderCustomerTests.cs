using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

public sealed class TenantProviderCustomerTests
{
    [Fact]
    public void Register_with_an_empty_email_fails()
    {
        var reference = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "cus_123").Value;

        var result = TenantProviderCustomer.Register(
            Guid.NewGuid(),
            PaymentProviderCode.Stripe,
            reference,
            "  ",
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantProviderCustomer.InvalidEmail", result.Error.Code);
    }

    [Fact]
    public void The_first_attached_method_becomes_default_automatically()
    {
        var customer = CreateCustomer();

        var result = customer.AttachPaymentMethod(
            "pm_123",
            "visa",
            "4242",
            12,
            2030,
            setAsDefault: false,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.NotNull(customer.GetDefaultMethod());
        Assert.Equal("pm_123", customer.GetDefaultMethod()!.MethodReference);
    }

    [Fact]
    public void A_second_method_does_not_become_default_unless_requested()
    {
        var customer = CreateCustomer();
        customer.AttachPaymentMethod("pm_123", "visa", "4242", 12, 2030, setAsDefault: false, DateTime.UtcNow);

        customer.AttachPaymentMethod("pm_456", "mastercard", "4444", 6, 2031, setAsDefault: false, DateTime.UtcNow);

        Assert.Equal("pm_123", customer.GetDefaultMethod()!.MethodReference);
        Assert.Equal(2, customer.SavedMethods.Count);
    }

    [Fact]
    public void Attaching_with_setAsDefault_switches_the_default()
    {
        var customer = CreateCustomer();
        customer.AttachPaymentMethod("pm_123", "visa", "4242", 12, 2030, setAsDefault: false, DateTime.UtcNow);

        customer.AttachPaymentMethod("pm_456", "mastercard", "4444", 6, 2031, setAsDefault: true, DateTime.UtcNow);

        Assert.Equal("pm_456", customer.GetDefaultMethod()!.MethodReference);
    }

    [Fact]
    public void Attaching_the_same_reference_twice_fails()
    {
        var customer = CreateCustomer();
        customer.AttachPaymentMethod("pm_123", "visa", "4242", 12, 2030, setAsDefault: false, DateTime.UtcNow);

        var result = customer.AttachPaymentMethod(
            "pm_123",
            "visa",
            "4242",
            12,
            2030,
            setAsDefault: false,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantProviderCustomer.MethodAlreadyAttached", result.Error.Code);
    }

    [Fact]
    public void Detaching_the_default_method_promotes_another_active_method()
    {
        var customer = CreateCustomer();
        var first = customer
            .AttachPaymentMethod("pm_123", "visa", "4242", 12, 2030, setAsDefault: false, DateTime.UtcNow)
            .Value;
        customer.AttachPaymentMethod("pm_456", "mastercard", "4444", 6, 2031, setAsDefault: false, DateTime.UtcNow);

        var result = customer.DetachPaymentMethod(first, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("pm_456", customer.GetDefaultMethod()!.MethodReference);
    }

    [Fact]
    public void Detaching_the_only_method_leaves_no_default()
    {
        var customer = CreateCustomer();
        var methodId = customer
            .AttachPaymentMethod("pm_123", "visa", "4242", 12, 2030, setAsDefault: false, DateTime.UtcNow)
            .Value;

        customer.DetachPaymentMethod(methodId, DateTime.UtcNow);

        Assert.Null(customer.GetDefaultMethod());
    }

    [Fact]
    public void Detaching_an_unknown_method_fails()
    {
        var customer = CreateCustomer();

        var result = customer.DetachPaymentMethod(Guid.NewGuid(), DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantProviderCustomer.MethodNotFound", result.Error.Code);
    }

    [Fact]
    public void MarkPaymentMethodAsDefault_switches_the_default_explicitly()
    {
        var customer = CreateCustomer();
        customer.AttachPaymentMethod("pm_123", "visa", "4242", 12, 2030, setAsDefault: false, DateTime.UtcNow);
        var second = customer
            .AttachPaymentMethod("pm_456", "mastercard", "4444", 6, 2031, setAsDefault: false, DateTime.UtcNow)
            .Value;

        var result = customer.MarkPaymentMethodAsDefault(second, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("pm_456", customer.GetDefaultMethod()!.MethodReference);
    }

    [Fact]
    public void ExpiresBefore_returns_true_once_the_expiration_month_has_passed()
    {
        var method = TenantSavedPaymentMethod.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "pm_123",
            "visa",
            "4242",
            6,
            2026,
            true,
            DateTime.UtcNow
        );

        Assert.True(method.ExpiresBefore(new DateTime(2026, 7, 1)));
        Assert.False(method.ExpiresBefore(new DateTime(2026, 6, 15)));
    }

    private static TenantProviderCustomer CreateCustomer()
    {
        var reference = ProviderCustomerReference.Create(PaymentProviderCode.Stripe, "cus_123").Value;
        return TenantProviderCustomer
            .Register(Guid.NewGuid(), PaymentProviderCode.Stripe, reference, "admin@acme.test", DateTime.UtcNow)
            .Value;
    }
}
