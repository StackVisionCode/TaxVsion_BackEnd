using BuildingBlocks.Common;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;

/// <summary>Mapea la factura al contrato de Documents (montos en dólares) y dispara la generación. La
/// URL estable de cobro ya la aseguró y persistió <c>EnsureInvoicePaymentLinkHandler</c> antes de este
/// paso, así que acá solo se lee. Un fallo lanza para que el RetryWithCooldown de Wolverine reintente
/// (Documents puede estar caído).</summary>
public static class GenerateInvoicePdfHandler
{
    public static async Task Handle(
        GenerateInvoicePdfCommand command,
        IInvoiceRepository invoices,
        IInvoiceDocumentClient documents,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        if (invoice is null || invoice.InvoiceNumber is null)
            return; // Nada que generar (borrador borrado o no emitido); no reintentar.

        // URL estable de cobro ya persistida por el paso Ensure. Si aún no hay (edge: factura pagada, o
        // el Ensure no corrió), el PDF sale sin botón — no lo bloqueamos acá.
        var activeLink = invoice.ActivePaymentLink;
        var paymentUrl = activeLink?.CheckoutUrl;

        var request = new InvoiceDocumentRequest(
            InvoiceId: invoice.Id,
            InvoiceNumber: invoice.InvoiceNumber,
            TaxYear: invoice.IssueDateUtc.Year,
            Currency: invoice.Currency,
            IssueDate: DateOnly.FromDateTime(invoice.IssueDateUtc),
            DueDate: DateOnly.FromDateTime(invoice.DueDateUtc),
            Issuer: MapIssuer(invoice.Issuer),
            Customer: MapCustomer(invoice.Customer),
            Lines: invoice
                .Lines.Select(l => new InvoiceDocLine(
                    l.Description,
                    l.Quantity,
                    ToDollars(l.UnitAmount.AmountCents),
                    ToDollars(l.UnitAmount.AmountCents * l.Quantity)
                ))
                .ToList(),
            Subtotal: ToDollars(invoice.Subtotal.AmountCents),
            TaxAmount: ToDollars(invoice.TaxTotal.AmountCents),
            Total: ToDollars(invoice.Total.AmountCents),
            Notes: invoice.Notes,
            Status: invoice.Status.ToString(),
            PaymentUrl: paymentUrl,
            PaidDate: invoice.PaidAtUtc is { } paid ? DateOnly.FromDateTime(paid) : null,
            ReceiptNumber: invoice.ReceiptNumber,
            ReceiptHash: invoice.ReceiptHash,
            Discount: ToDollars(invoice.DiscountTotal.AmountCents),
            Adjustments: invoice
                .Adjustments.Select(a => new InvoiceDocAdjustment(
                    string.IsNullOrWhiteSpace(a.Code) ? a.Type.ToString() : $"{a.Type} · {a.Code}",
                    ToDollars(a.Amount.AmountCents)
                ))
                .ToList(),
            SettlementType: invoice.SettlementType?.ToString()
        );

        // Idempotencia versionada (punto 8 del review): incluye el ESTADO, la versión y el payable — así
        // la versión PAGADA (con marca de agua + recibo) se genera como PDF nuevo en vez de devolver el
        // no-pagado cacheado. Un cambio de plantilla/idioma/datos fiscales requeriría subir la versión.
        var payablePart = activeLink is null ? "nolink" : activeLink.ExternalPayableId.ToString("N");
        var statusPart = invoice.Status == InvoiceStatus.Paid ? "paid" : "v1";
        var idempotencyKey = $"invoice-pdf:{invoice.Id:N}:{statusPart}:{payablePart}";
        var result = await documents.GenerateAsync(
            request,
            command.TenantId,
            idempotencyKey,
            correlation.CorrelationId,
            ct
        );
        if (result.IsFailure)
            throw new InvalidOperationException(
                $"Documents generation failed: {result.Error.Code} - {result.Error.Message}"
            );
    }

    private static decimal ToDollars(long cents) => cents / 100m;

    private static InvoiceDocParty MapCustomer(CustomerSnapshot customer) =>
        new(customer.Name, customer.TaxId ?? string.Empty, FormatAddress(customer.Billing));

    private static InvoiceDocParty MapIssuer(IssuerSnapshot? issuer) =>
        issuer is null
            ? new InvoiceDocParty("—", string.Empty, null)
            : new InvoiceDocParty(issuer.Name, issuer.TaxId ?? string.Empty, FormatAddress(issuer.Address));

    private static string? FormatAddress(Address? address)
    {
        if (address is null)
            return null;
        var line2 = string.IsNullOrWhiteSpace(address.Line2) ? string.Empty : $", {address.Line2}";
        return $"{address.Line1}{line2}, {address.City}, {address.State} {address.Zip}, {address.Country}";
    }
}
