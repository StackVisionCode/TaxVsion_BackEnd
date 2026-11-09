using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Domain;

/// <summary>Marcas de sincronización del enlace y del stock (BIL-3b): no cuentan como cambio de la factura.</summary>
public sealed class InvoiceSyncMarkerTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MarkPaymentLinkSynced_StoresDue_WithoutTouchingUpdatedAt()
    {
        var invoice = Issued();
        var updatedAt = invoice.UpdatedAtUtc;

        invoice.MarkPaymentLinkSynced(invoice.AmountDue);

        Assert.Equal(invoice.AmountDue, invoice.PaymentLinkSyncedDue);
        Assert.Equal(updatedAt, invoice.UpdatedAtUtc);
    }

    [Fact]
    public void MarkStockSynced_StoresVersion_WithoutTouchingUpdatedAt()
    {
        var invoice = Issued();
        var updatedAt = invoice.UpdatedAtUtc;

        invoice.MarkStockSynced(updatedAt);

        Assert.Equal(updatedAt, invoice.StockSyncedAtUtc);
        Assert.Equal(updatedAt, invoice.UpdatedAtUtc);
    }

    private static Invoice Issued()
    {
        var invoice = Invoice
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax preparation", 1, 15_000, 0)],
                null,
                Now
            )
            .Value;
        Assert.True(invoice.Issue(InvoiceNumber.Create("INV-2026-00001").Value, Now, Now.AddDays(30), Guid.NewGuid()).IsSuccess);
        return invoice;
    }
}
