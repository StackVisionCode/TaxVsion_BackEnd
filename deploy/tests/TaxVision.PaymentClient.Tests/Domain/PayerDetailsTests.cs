using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Domain;

public sealed class PayerDetailsTests
{
    [Fact]
    public void Create_keeps_the_trimmed_receipt_email()
    {
        var payment = CreatePayment(receiptEmail: "  client@example.com ");

        Assert.Equal("client@example.com", payment.Payer.ReceiptEmail);
        Assert.Null(payment.Payer.PayerName);
        Assert.Null(payment.Payer.CardLast4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("a b@example.com")]
    [InlineData("@example.com")]
    [InlineData("client@")]
    public void Create_discards_a_missing_or_invalid_receipt_email_without_failing(string? receiptEmail)
    {
        var payment = CreatePayment(receiptEmail);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.Payer.ReceiptEmail);
    }

    [Fact]
    public void Create_discards_a_receipt_email_longer_than_the_limit()
    {
        var tooLong = new string('a', PayerDetails.EmailMaxLength) + "@example.com";

        Assert.Null(CreatePayment(tooLong).Payer.ReceiptEmail);
    }

    [Fact]
    public void CreateAlreadySucceeded_also_keeps_the_receipt_email()
    {
        var payment = TenantPayment
            .CreateAlreadySucceeded(
                Guid.NewGuid(),
                IdempotencyKey.Create("key-zero").Value,
                Money.Zero("USD"),
                null,
                PaymentPurpose.Create(PaymentPurposeKind.Other, null).Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                DateTime.UtcNow,
                "client@example.com"
            )
            .Value;

        Assert.Equal("client@example.com", payment.Payer.ReceiptEmail);
    }

    [Fact]
    public void RecordProviderPayerDetails_stores_what_the_provider_returned()
    {
        var payment = CreatePayment(receiptEmail: "client@example.com");

        payment.RecordProviderPayerDetails(" Jane Doe ", "jane@paypal.example", "visa", "4242");

        Assert.Equal("client@example.com", payment.Payer.ReceiptEmail);
        Assert.Equal("Jane Doe", payment.Payer.PayerName);
        Assert.Equal("jane@paypal.example", payment.Payer.PayerEmail);
        Assert.Equal("visa", payment.Payer.CardBrand);
        Assert.Equal("4242", payment.Payer.CardLast4);
    }

    [Fact]
    public void RecordProviderPayerDetails_discards_each_invalid_value_on_its_own()
    {
        var payment = CreatePayment();

        payment.RecordProviderPayerDetails(
            new string('n', PayerDetails.NameMaxLength + 1),
            "broken-email",
            new string('b', PayerDetails.CardBrandMaxLength + 1),
            "42x2"
        );

        Assert.Null(payment.Payer.PayerName);
        Assert.Null(payment.Payer.PayerEmail);
        Assert.Null(payment.Payer.CardBrand);
        Assert.Null(payment.Payer.CardLast4);
    }

    [Theory]
    [InlineData("424")]
    [InlineData("42424")]
    [InlineData("４２４２")]
    public void RecordProviderPayerDetails_only_accepts_four_ascii_digits_as_last4(string last4)
    {
        var payment = CreatePayment();

        payment.RecordProviderPayerDetails(null, null, null, last4);

        Assert.Null(payment.Payer.CardLast4);
    }

    [Fact]
    public void RecordProviderPayerDetails_keeps_previous_values_when_the_new_ones_are_missing()
    {
        var payment = CreatePayment();
        payment.RecordProviderPayerDetails("Jane Doe", "jane@example.com", "visa", "4242");

        payment.RecordProviderPayerDetails(null, "  ", null, "bad");

        Assert.Equal("Jane Doe", payment.Payer.PayerName);
        Assert.Equal("jane@example.com", payment.Payer.PayerEmail);
        Assert.Equal("visa", payment.Payer.CardBrand);
        Assert.Equal("4242", payment.Payer.CardLast4);
    }

    [Fact]
    public void Each_payment_gets_its_own_payer_instance()
    {
        // EF no admite compartir un owned entre filas.
        Assert.NotSame(CreatePayment().Payer, CreatePayment().Payer);
    }

    private static TenantPayment CreatePayment(string? receiptEmail = null) =>
        TenantPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create($"key-{Guid.NewGuid():N}").Value,
                Money.Create(2500, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.Other, null).Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                DateTime.UtcNow,
                receiptEmail
            )
            .Value;
}
