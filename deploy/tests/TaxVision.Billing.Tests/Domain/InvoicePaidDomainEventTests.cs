using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Invoices.Events;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Domain;

/// <summary>La factura registra InvoicePaidDomainEvent solo al pasar a Paid, con los datos que lleva el aviso.</summary>
public sealed class InvoicePaidDomainEventTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PaymentThatCoversTotal_RaisesOnePaidEvent()
    {
        var invoice = IssuedInvoice();

        Pay(invoice, "a", 4_000, InvoicePaymentSource.Online);
        Pay(invoice, "b", 6_000, InvoicePaymentSource.Online);

        var paid = Assert.IsType<InvoicePaidDomainEvent>(Assert.Single(invoice.DomainEvents));
        Assert.Equal(invoice.Id, paid.InvoiceId);
        Assert.Equal(invoice.TenantId, paid.TenantId);
        Assert.Equal("INV-2026-00001", paid.InvoiceNumber);
        Assert.Equal(10_000, paid.AmountPaidCents);
        Assert.Equal(PaymentMethod.Online, paid.PaymentMethod);
        Assert.Equal(invoice.PaidAtUtc, paid.PaidAtUtc);
        Assert.Equal(InvoicePaymentSource.Online, paid.PaymentSource);
    }

    [Fact]
    public void PaymentOnPaidInvoice_OrSamePaymentAgain_RaisesNothingMore()
    {
        var invoice = IssuedInvoice();
        Pay(invoice, "a", 10_000, InvoicePaymentSource.Manual);

        Pay(invoice, "a", 10_000, InvoicePaymentSource.Manual);
        Pay(invoice, "b", 1_000, InvoicePaymentSource.Manual);

        Assert.Single(invoice.DomainEvents);
    }

    [Fact]
    public void EditThatLeavesInvoiceCovered_RaisesPaidEventWithoutSource()
    {
        var invoice = IssuedInvoice();
        Pay(invoice, "a", 4_000, InvoicePaymentSource.Manual);

        var edited = invoice.Edit(
            invoice.Customer,
            "USD",
            [new DraftInvoiceLine("Tax preparation", 1, 4_000, 0)],
            null,
            Now,
            Guid.NewGuid()
        );

        Assert.True(edited.IsSuccess);
        var paid = Assert.IsType<InvoicePaidDomainEvent>(Assert.Single(invoice.DomainEvents));
        Assert.Null(paid.PaymentSource);
    }

    private static void Pay(Invoice invoice, string reference, long cents, InvoicePaymentSource source) =>
        Assert.True(
            invoice
                .ApplyPayment(
                    source,
                    reference,
                    Money.Create(cents, "USD").Value,
                    PaymentMethod.Online,
                    Now,
                    Guid.Empty
                )
                .IsSuccess
        );

    private static Invoice IssuedInvoice()
    {
        var invoice = Invoice
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 10_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(
            invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), Guid.NewGuid()).IsSuccess
        );
        return invoice;
    }
}
