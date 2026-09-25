using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.GetInvoice;

namespace TaxVision.Billing.Application.Invoices.ListInvoices;

public sealed record ListInvoicesQuery(Guid TenantId, Guid ActorUserId, bool CanViewAll, int Take = 50);

public static class ListInvoicesHandler
{
    public static async Task<Result<IReadOnlyList<InvoiceSummaryResponse>>> Handle(
        ListInvoicesQuery query,
        IInvoiceRepository invoices,
        IOptions<BillingVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        // Filtro por asignación solo si el flag está ON y el actor no ve todo (view_all/admin).
        var assignedTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        var list = await invoices.ListByTenantAsync(query.TenantId, query.Take, ct, assignedTo);

        IReadOnlyList<InvoiceSummaryResponse> response = list.Select(invoice => new InvoiceSummaryResponse(
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.Status.ToString(),
                invoice.Currency,
                invoice.Subtotal.AmountCents,
                invoice.TaxTotal.AmountCents,
                invoice.Total.AmountCents,
                invoice.AmountDue.AmountCents,
                invoice.AmountPaid.AmountCents,
                invoice.PdfFileId,
                invoice.CreatedAtUtc,
                invoice.PaidAtUtc,
                invoice.PaymentMethod?.ToString(),
                invoice.ReceiptNumber,
                invoice.ReceiptHash,
                invoice.ActivePaymentLink?.CheckoutUrl
            ))
            .ToList();

        return Result.Success(response);
    }
}
