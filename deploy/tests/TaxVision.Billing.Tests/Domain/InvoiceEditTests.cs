using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Domain;

/// <summary>Edición libre de facturas con pagos (D-BIL-1), con las invariantes de cobro de BIL-4.</summary>
public sealed class InvoiceEditTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly DateTime Issued = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PaidAt = new(2026, 11, 2, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime EditedAt = new(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EditPaidInvoice_HigherTotal_BecomesPartiallyPaid()
    {
        // Caso INV-2026-00002: pagada y editada al alza quedó "Paid" con AmountDue 150000.
        var invoice = IssuedInvoice(150_000);
        Pay(invoice, 150_000, PaymentMethod.Cash);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        var result = Edit(invoice, 300_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(150_000, invoice.AmountDue.AmountCents);
        Assert.Null(invoice.ReceiptNumber);
        Assert.Null(invoice.ReceiptHash);
        Assert.Null(invoice.PaidAtUtc);
        var change = invoice.StatusChanges.Last();
        Assert.Equal(
            (InvoiceStatus.Paid, InvoiceStatus.PartiallyPaid, StatusChangeTrigger.Edit),
            (change.FromStatus, change.ToStatus, change.Trigger)
        );
    }

    [Fact]
    public void EditWithPayments_ChangeCurrency_IsRejected()
    {
        var invoice = IssuedInvoice(30_000);
        Pay(invoice, 10_000, PaymentMethod.Cash);
        var customerName = invoice.Customer.Name;

        var result = invoice.Edit(
            Customer("Someone else"),
            "EUR",
            [new DraftInvoiceLine("Tax preparation", 1, 30_000, 0)],
            "changed",
            EditedAt,
            ActorUserId
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.CurrencyLockedByPayments", result.Error.Code);
        Assert.Equal("USD", invoice.Currency);
        Assert.Equal(Money.Create(10_000, "USD").Value, invoice.AmountPaid);
        Assert.Equal(customerName, invoice.Customer.Name);
        Assert.Null(invoice.Notes);
    }

    [Fact]
    public void EditPartiallyPaid_LowerTotal_BecomesPaid_WithReceipt()
    {
        var invoice = IssuedInvoice(30_000);
        Pay(invoice, 10_000, PaymentMethod.Card);
        Assert.Null(invoice.ReceiptNumber);

        var result = Edit(invoice, 10_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0, invoice.AmountDue.AmountCents);
        Assert.Equal($"REC-{invoice.InvoiceNumber}", invoice.ReceiptNumber);
        Assert.False(string.IsNullOrEmpty(invoice.ReceiptHash));
        Assert.Equal(PaidAt, invoice.PaidAtUtc);
        Assert.Equal(PaymentMethod.Card, invoice.PaymentMethod);
        var change = invoice.StatusChanges.Last();
        Assert.Equal(
            (InvoiceStatus.PartiallyPaid, InvoiceStatus.Paid, StatusChangeTrigger.Edit),
            (change.FromStatus, change.ToStatus, change.Trigger)
        );
    }

    [Fact]
    public void EditPartiallyPaid_BelowPaid_KeepsOverpaymentVisible()
    {
        var invoice = IssuedInvoice(30_000);
        Pay(invoice, 20_000, PaymentMethod.Cash);

        var result = Edit(invoice, 15_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(20_000, invoice.AmountPaid.AmountCents);
        Assert.Equal(15_000, invoice.Total.AmountCents);
        Assert.Equal(0, invoice.AmountDue.AmountCents);
    }

    [Fact]
    public void EditPaidWithoutPayments_HigherTotal_BecomesIssued()
    {
        // Onboarding cubierto al 100 %: Paid con $0 cobrados. Si sube el total no hay pago que la haga "parcial".
        var invoice = Invoice
            .CreateForOnboarding(
                TenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                InvoiceNumber.Create("ONB-2026-00001").Value,
                Customer("Client"),
                new IssuerSnapshot("TaxVision", new Address("1 Main", null, "City", "ST", "00000", "US"), null, null, null, null),
                "Enterprise (Monthly)",
                12_900,
                12_900,
                0,
                "USD",
                SettlementType.FullyCoveredByCode,
                [new OnboardingInvoiceAdjustment(InvoiceAdjustmentType.Gift, "WELCOME100", Guid.NewGuid(), 12_900)],
                Issued,
                Issued
            )
            .Value;

        var result = Edit(invoice, 12_900);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(12_900, invoice.AmountDue.AmountCents);
        Assert.Null(invoice.ReceiptNumber);
        Assert.Equal(StatusChangeTrigger.Edit, invoice.StatusChanges.Last().Trigger);
    }

    [Fact]
    public void EditWithoutPayments_ChangeCurrency_IsAllowed()
    {
        var invoice = IssuedInvoice(10_000);

        var result = invoice.Edit(
            Customer("Client"),
            "EUR",
            [new DraftInvoiceLine("Tax preparation", 1, 10_000, 0)],
            null,
            EditedAt,
            ActorUserId
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("EUR", invoice.Currency);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
    }

    [Fact]
    public void EditPaid_SameTotal_DoesNotAudit()
    {
        var invoice = IssuedInvoice(10_000);
        Pay(invoice, 10_000, PaymentMethod.Cash);
        var receipt = invoice.ReceiptHash;
        var changes = invoice.StatusChanges.Count;

        var result = Edit(invoice, 10_000);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(receipt, invoice.ReceiptHash);
        Assert.Equal(changes, invoice.StatusChanges.Count);
    }

    private static BuildingBlocks.Results.Result Edit(Invoice invoice, long totalCents) =>
        invoice.Edit(
            Customer("Client"),
            "USD",
            [new DraftInvoiceLine("Tax preparation", 1, totalCents, 0)],
            null,
            EditedAt,
            ActorUserId
        );

    private static void Pay(Invoice invoice, long cents, PaymentMethod method) =>
        Assert.True(
            invoice
                .ApplyPayment(
                    InvoicePaymentSource.Manual,
                    Guid.NewGuid().ToString(),
                    Money.Create(cents, "USD").Value,
                    method,
                    PaidAt,
                    ActorUserId
                )
                .IsSuccess
        );

    private static CustomerSnapshot Customer(string name) => new(Guid.NewGuid(), name, "client@example.com", null, null, null);

    private static Invoice IssuedInvoice(long totalCents)
    {
        var invoice = Invoice
            .CreateDraft(
                TenantId,
                ActorUserId,
                Customer("Client"),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, totalCents, 0)],
                null,
                Issued
            )
            .Value;
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00002").Value, Issued, Issued.AddDays(30), ActorUserId).IsSuccess);
        return invoice;
    }
}
