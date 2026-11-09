using TaxVision.Billing.Domain.Payments;
using Xunit;

namespace TaxVision.Billing.Tests.Domain;

public sealed class UnappliedPaymentTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Resolve_Open_RecordsResolution()
    {
        var payment = NewPayment();
        var actor = Guid.NewGuid();

        var result = payment.Resolve(UnappliedPaymentResolution.Refunded, "Refunded in Stripe", actor, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(UnappliedPaymentResolution.Refunded, payment.Resolution);
        Assert.Equal("Refunded in Stripe", payment.ResolutionNote);
        Assert.Equal(actor, payment.ResolvedByUserId);
        Assert.Equal(Now, payment.ResolvedAtUtc);
    }

    [Fact]
    public void Resolve_AlreadyResolved_ReturnsError()
    {
        var payment = NewPayment();
        Assert.True(payment.Resolve(UnappliedPaymentResolution.Refunded, "Refunded", Guid.NewGuid(), Now).IsSuccess);

        var result = payment.Resolve(UnappliedPaymentResolution.AppliedAsCredit, "Credit", Guid.NewGuid(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.UnappliedPayment.AlreadyResolved", result.Error.Code);
        Assert.Equal(UnappliedPaymentResolution.Refunded, payment.Resolution);
    }

    [Fact]
    public void Resolve_WithoutNote_ReturnsError()
    {
        var payment = NewPayment();

        var result = payment.Resolve(UnappliedPaymentResolution.Refunded, "  ", Guid.NewGuid(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.UnappliedPayment.NoteRequired", result.Error.Code);
        Assert.Null(payment.ResolvedAtUtc);
    }

    [Fact]
    public void Record_InvalidCurrency_StoresUnknownCurrency()
    {
        var payment = UnappliedPayment.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "not-a-guid",
            1_000,
            "dollars",
            UnappliedPaymentReason.InvalidReference,
            Now
        );

        Assert.Equal(UnappliedPayment.UnknownCurrency, payment.Currency);
    }

    private static UnappliedPayment NewPayment() =>
        UnappliedPayment.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            10_000,
            "USD",
            UnappliedPaymentReason.InvoiceVoided,
            Now
        );
}
