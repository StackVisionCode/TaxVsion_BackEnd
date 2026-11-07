using System.Globalization;
using System.Net;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;

/// <summary>
/// Arma el diccionario de render de la factura, sin I/O. Fluid resuelve el acceso por clave (.number) solo
/// sobre IDictionary&lt;string, object&gt;: por eso los diccionarios son object (no object?) y los nulos se
/// colapsan a "" antes de renderizar. Los montos llegan ya calculados; acá solo se formatean (cultura invariante).
/// </summary>
public static class InvoiceRenderModelBuilder
{
    private const string DefaultBrandColor = "#2563eb";

    public static IReadOnlyDictionary<string, object?> Build(
        ProcessInvoiceGenerationCommand command,
        BrandingPayload? branding,
        string platformBrand,
        IQrCodeGenerator qrGenerator
    )
    {
        var invoice = command.Invoice;
        var culture = CultureInfo.InvariantCulture;

        // El link de pago lo arma Billing con el subdominio del tenant; Documents solo lo acepta si es una
        // URL absoluta http/https (fail-closed: si no lo es, no se dibuja botón ni QR). El QR codifica esa
        // misma URL — hereda el subdominio automáticamente. Pagada ⇒ nunca se ofrece pago.
        var paymentUrl =
            IsRenderablePaymentUrl(invoice.PaymentUrl) && invoice.Status != "Paid" ? invoice.PaymentUrl! : string.Empty;
        var paymentQr = paymentUrl.Length > 0 ? qrGenerator.CreatePngDataUri(paymentUrl) : string.Empty;

        var logo = BrandingRenderValues.EmbeddedLogo(branding?.LogoDataUri);
        var brandColor = BrandingRenderValues.IsHexColor(branding?.BrandColorHex)
            ? branding!.BrandColorHex!
            : DefaultBrandColor;
        var displayName = string.IsNullOrWhiteSpace(branding?.DisplayName)
            ? invoice.Issuer.Name
            : branding!.DisplayName!;
        var footer = ResolveFooter(invoice.InvoiceFooter, branding?.FooterText, platformBrand, command.InvoiceNumber);

        var lines = invoice
            .Lines.Select(
                object (line) =>
                    new Dictionary<string, object>
                    {
                        ["description"] = line.Description,
                        ["quantity"] = line.Quantity.ToString("0.##", culture),
                        ["unitPrice"] = line.UnitPrice.ToString("N2", culture),
                        ["amount"] = line.Amount.ToString("N2", culture),
                    }
            )
            .ToList();

        // Ajustes de onboarding (descuentos por código): una fila negativa por beneficio.
        var adjustments = (invoice.Adjustments ?? [])
            .Select(
                object (adj) =>
                    new Dictionary<string, object>
                    {
                        ["label"] = adj.Label,
                        ["amount"] = adj.Amount.ToString("N2", culture),
                    }
            )
            .ToList();

        return new Dictionary<string, object?>
        {
            ["invoice"] = new Dictionary<string, object>
            {
                ["number"] = command.InvoiceNumber,
                ["taxYear"] = command.TaxYear,
                ["currency"] = invoice.Currency,
                ["issueDate"] = invoice.IssueDate.ToString("yyyy-MM-dd", culture),
                ["dueDate"] = invoice.DueDate?.ToString("yyyy-MM-dd", culture) ?? string.Empty,
                ["issuer"] = PartyToDict(invoice.Issuer),
                ["customer"] = PartyToDict(invoice.Customer),
                ["lines"] = lines,
                ["subtotal"] = invoice.Subtotal.ToString("N2", culture),
                ["taxAmount"] = invoice.TaxAmount.ToString("N2", culture),
                ["total"] = invoice.Total.ToString("N2", culture),
                ["notes"] = invoice.Notes ?? string.Empty,
                // Onboarding con código: ajustes (negativos), descuento total y tipo de liquidación.
                ["adjustments"] = adjustments,
                ["discount"] = invoice.Discount.ToString("N2", culture),
                ["hasDiscount"] = invoice.Discount > 0m,
                ["settlementType"] = invoice.SettlementType ?? string.Empty,
                // Estado + cobro (dato de Billing). La plantilla decide watermark y visibilidad del botón.
                ["status"] = string.IsNullOrWhiteSpace(invoice.Status) ? "Pending" : invoice.Status,
                ["paidDate"] = invoice.PaidDate?.ToString("yyyy-MM-dd", culture) ?? string.Empty,
                ["paymentUrl"] = paymentUrl,
                ["paymentQr"] = paymentQr,
                // Comprobante (aparece en la versión Pagada): número de recibo + hash de verificación.
                ["receiptNumber"] = invoice.ReceiptNumber ?? string.Empty,
                ["receiptHash"] = invoice.ReceiptHash ?? string.Empty,
                // Ajustes de facturación (dato de Billing). Vacío = la plantilla no los dibuja.
                ["paymentTerms"] = invoice.PaymentTerms?.Trim() ?? string.Empty,
                ["paymentInstructions"] = invoice.PaymentInstructions?.Trim() ?? string.Empty,
                // Sin pagos, la plantilla no dibuja lo pagado ni el saldo.
                ["hasPayments"] = invoice.AmountPaid > 0m,
                ["amountPaid"] = invoice.AmountPaid.ToString("N2", culture),
                ["amountDue"] = invoice.AmountDue.ToString("N2", culture),
                // Branding del tenant (opcional). La plantilla los aplica; si vienen vacíos, look por defecto.
                ["logo"] = logo,
                ["brandColor"] = brandColor,
                ["displayName"] = displayName,
                ["footer"] = footer,
            },
        };
    }

    // Merge campo a campo: lo que venga en el request gana; si no, el perfil guardado del tenant; si no,
    // null (Build aplica el default por campo). Así el tenant configura una vez y puede sobrescribir algo
    // puntual en un request sin tocar su perfil.
    public static BrandingPayload? MergeBranding(BrandingPayload? request, DocumentBranding? stored)
    {
        if (request is null && stored is null)
            return null;

        return new BrandingPayload(
            DisplayName: Coalesce(request?.DisplayName, stored?.DisplayName),
            LogoDataUri: Coalesce(request?.LogoDataUri, stored?.LogoDataUri),
            BrandColorHex: Coalesce(request?.BrandColorHex, stored?.BrandColorHex),
            FooterText: Coalesce(request?.FooterText, stored?.FooterText)
        );
    }

    // Pie: el de los ajustes de facturación gana; si no, el override de la marca; si no, la línea de
    // siempre. El de facturación es texto del tenant: se codifica (la plantilla imprime el pie sin escapar)
    // y sus saltos de línea pasan a <br />.
    private static string ResolveFooter(
        string? invoiceFooter,
        string? brandingFooter,
        string platformBrand,
        string invoiceNumber
    )
    {
        if (!string.IsNullOrWhiteSpace(invoiceFooter))
            return WebUtility.HtmlEncode(invoiceFooter.Trim()).ReplaceLineEndings("<br />");
        return string.IsNullOrWhiteSpace(brandingFooter)
            ? $"Document generated by {platformBrand} · Invoice {invoiceNumber}"
            : brandingFooter;
    }

    private static Dictionary<string, object> PartyToDict(InvoiceParty party) =>
        new()
        {
            ["name"] = party.Name,
            ["taxId"] = party.TaxId,
            ["address"] = party.Address ?? string.Empty,
            ["phone"] = party.Phone?.Trim() ?? string.Empty,
            ["email"] = party.Email?.Trim() ?? string.Empty,
            ["website"] = party.Website?.Trim() ?? string.Empty,
        };

    private static string? Coalesce(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static bool IsRenderablePaymentUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
