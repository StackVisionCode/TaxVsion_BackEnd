using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Application.Invoices.GetInvoice;

public sealed record GetInvoiceQuery(Guid TenantId, Guid InvoiceId);

public sealed record InvoiceSummaryResponse(
    Guid Id,
    string? InvoiceNumber,
    string Status,
    string Currency,
    long SubtotalCents,
    long TaxTotalCents,
    long TotalCents,
    long AmountDueCents,
    long AmountPaidCents,
    Guid? PdfFileId,
    DateTime CreatedAtUtc,
    DateTime? PaidAtUtc,
    // Método con el que se pagó (Online/Card/Cash/Check/BankTransfer/Other), null si no está pagada.
    string? PaymentMethod,
    // Comprobante: número de recibo + hash de verificación (SHA-256), null si no está pagada.
    string? ReceiptNumber,
    string? ReceiptHash,
    // URL estable de cobro (para "enviar a pagar" / botón del PDF). Null hasta que se emite y se asegura
    // el link. La compone PaymentClient; Billing solo la guarda y la expone.
    string? CheckoutUrl
);

public static class GetInvoiceHandler
{
    public static async Task<Result<InvoiceSummaryResponse>> Handle(
        GetInvoiceQuery query,
        IInvoiceRepository invoices,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(query.TenantId, query.InvoiceId, ct);
        if (invoice is null)
            return Result.Failure<InvoiceSummaryResponse>(
                new Error("Billing.Invoice.NotFound", "Invoice does not exist.")
            );

        return Result.Success(
            new InvoiceSummaryResponse(
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
            )
        );
    }
}
