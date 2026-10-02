using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Billing.Tests.Invoices;

public sealed class InvoicePaymentLinkTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void AttachPaymentLink_repairs_checkout_url_for_existing_payable()
    {
        var invoice = CreateDraft();
        var payableId = Guid.NewGuid();
        var staleUrl = "https://manfer.taxproffice.com/pay/stale-reference";
        var stableUrl = "https://manfer.taxproffice.com/payments-client/invoices/stable-reference";

        var first = invoice.AttachPaymentLink(payableId, staleUrl, Now);
        var second = invoice.AttachPaymentLink(payableId, stableUrl, Now.AddMinutes(1));

        Assert.Same(first, second);
        Assert.Single(invoice.PaymentLinks);
        Assert.Equal(stableUrl, invoice.ActivePaymentLink?.CheckoutUrl);
    }

    private static Invoice CreateDraft()
    {
        var result = Invoice.CreateDraft(
            tenantId: Guid.NewGuid(),
            actorUserId: Guid.NewGuid(),
            customer: new CustomerSnapshot(Guid.NewGuid(), "Client", "client@example.com", null, null, null),
            currency: "USD",
            lines: [new DraftInvoiceLine("Tax prep", 1, 10000, 0)],
            notes: null,
            nowUtc: Now
        );

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
