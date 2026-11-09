using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Domain;

public sealed class InvoicePaymentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ApplyPayment_TwoManualPartials_SumsAmountPaid()
    {
        var invoice = IssuedInvoice(15_000);

        Assert.True(Apply(invoice, InvoicePaymentSource.Manual, "a", 5_000).IsSuccess);
        Assert.True(Apply(invoice, InvoicePaymentSource.Manual, "b", 10_000).IsSuccess);

        Assert.Equal(15_000, invoice.AmountPaid.AmountCents);
        Assert.Equal(0, invoice.AmountDue.AmountCents);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void ApplyPayment_SameSourceReferenceTwice_IsNoOp()
    {
        var invoice = IssuedInvoice(15_000);

        Assert.True(Apply(invoice, InvoicePaymentSource.Online, "x", 5_000).IsSuccess);
        Assert.True(Apply(invoice, InvoicePaymentSource.Online, "x", 5_000).IsSuccess);

        Assert.Equal(5_000, invoice.AmountPaid.AmountCents);
        Assert.Single(invoice.Payments);
    }

    [Fact]
    public void ApplyPayment_OnPaidInvoice_RecordsOverpayment()
    {
        var invoice = IssuedInvoice(10_000);
        Assert.True(Apply(invoice, InvoicePaymentSource.Online, "first", 10_000).IsSuccess);

        Assert.True(Apply(invoice, InvoicePaymentSource.Online, "y", 10_000).IsSuccess);

        Assert.Equal(20_000, invoice.AmountPaid.AmountCents);
        Assert.Equal(10_000, invoice.Payments.Last().ExceedsDueCents);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void Reissue_CarriedCreditThenOnlineDue_TotalsMatch()
    {
        var invoice = DraftInvoice(15_000);
        Assert.True(invoice.LinkAsReplacementFor(Guid.NewGuid(), 10_000, Now).IsSuccess);
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00002").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess);
        Assert.True(invoice.ApplyCarriedCredit(Now).IsSuccess);
        Assert.Equal(5_000, invoice.AmountDue.AmountCents);

        Assert.True(Apply(invoice, InvoicePaymentSource.Online, "z", 5_000).IsSuccess);

        Assert.Equal(15_000, invoice.AmountPaid.AmountCents);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(2, invoice.Payments.Count);
    }

    private static BuildingBlocks.Results.Result Apply(
        Invoice invoice,
        InvoicePaymentSource source,
        string reference,
        long cents
    ) =>
        invoice.ApplyPayment(
            source,
            reference,
            Money.Create(cents, "USD").Value,
            source == InvoicePaymentSource.Online ? PaymentMethod.Online : PaymentMethod.Cash,
            Now,
            ActorUserId
        );

    private static Invoice IssuedInvoice(long totalCents)
    {
        var invoice = DraftInvoice(totalCents);
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), ActorUserId).IsSuccess);
        return invoice;
    }

    private static Invoice DraftInvoice(long totalCents)
    {
        var result = Invoice.CreateDraft(
            TenantId,
            ActorUserId,
            new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
            "USD",
            [new DraftInvoiceLine("Tax preparation", 1, totalCents, 0)],
            null,
            Now
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
