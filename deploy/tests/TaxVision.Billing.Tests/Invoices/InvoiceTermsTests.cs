using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

/// <summary>Condiciones de cobro congeladas en la factura (Invoice): snapshot y vencimiento derivado.</summary>
public sealed class InvoiceTermsTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static Invoice Draft(InvoiceTermsSnapshot? terms)
    {
        var result = Invoice.CreateDraft(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CustomerSnapshot(Guid.NewGuid(), "Client", null, null, null, null),
            "USD",
            [new DraftInvoiceLine("Tax prep", 1, 10_000, 0)],
            null,
            Now,
            null,
            terms
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void The_draft_freezes_the_terms()
    {
        var invoice = Draft(new InvoiceTermsSnapshot(PaymentTerms.Net15, "Zelle", "Thanks"));

        Assert.Equal(PaymentTerms.Net15, invoice.PaymentTerms);
        Assert.Equal("Zelle", invoice.PaymentInstructions);
        Assert.Equal("Thanks", invoice.InvoiceFooter);
        Assert.Equal(new InvoiceTermsSnapshot(PaymentTerms.Net15, "Zelle", "Thanks"), invoice.GetTermsSnapshot());
    }

    [Theory]
    [InlineData(PaymentTerms.DueOnReceipt, 0)]
    [InlineData(PaymentTerms.Net7, 7)]
    [InlineData(PaymentTerms.Net45, 45)]
    public void The_due_date_follows_the_frozen_terms(PaymentTerms terms, int days)
    {
        var invoice = Draft(new InvoiceTermsSnapshot(terms, null, null));

        Assert.Equal(Now.AddDays(days), invoice.DueDateFor(Now));
    }

    [Fact]
    public void An_invoice_without_frozen_terms_keeps_the_legacy_30_days()
    {
        var invoice = Draft(null);

        Assert.Null(invoice.PaymentTerms);
        Assert.Null(invoice.GetTermsSnapshot());
        Assert.Equal(Now.AddDays(Invoice.LegacyNetDays), invoice.DueDateFor(Now));
    }
}
