using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Settings;

public sealed class PaymentTermsTests
{
    [Theory]
    [InlineData(PaymentTerms.DueOnReceipt, 0, "Due on receipt")]
    [InlineData(PaymentTerms.Net7, 7, "Net 7")]
    [InlineData(PaymentTerms.Net15, 15, "Net 15")]
    [InlineData(PaymentTerms.Net30, 30, "Net 30")]
    [InlineData(PaymentTerms.Net45, 45, "Net 45")]
    public void Each_term_has_its_days_and_label(PaymentTerms terms, int days, string label)
    {
        Assert.Equal(days, terms.NetDays());
        Assert.Equal(label, terms.Label());
    }

    [Theory]
    [InlineData("Net30", PaymentTerms.Net30)]
    [InlineData(" net30 ", PaymentTerms.Net30)]
    [InlineData("DueOnReceipt", PaymentTerms.DueOnReceipt)]
    public void The_enum_name_parses_case_insensitively(string raw, PaymentTerms expected)
    {
        Assert.True(PaymentTermsExtensions.TryParse(raw, out var terms));
        Assert.Equal(expected, terms);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("3")]
    [InlineData("99")]
    [InlineData("Net60")]
    [InlineData("Net 30")]
    public void Anything_else_is_rejected(string? raw)
    {
        Assert.False(PaymentTermsExtensions.TryParse(raw, out _));
    }
}
