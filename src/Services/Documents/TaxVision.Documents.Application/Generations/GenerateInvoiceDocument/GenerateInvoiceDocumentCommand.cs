namespace TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;

/// <summary>
/// Primer slice E2E: generar el PDF de una factura. Contrato fuerte (Issuer/Customer/Lines/Totals
/// tipados) — nada de object/dynamic. Documents NO recalcula ni valida reglas fiscales: recibe los
/// datos ya validados por el dueño (Billing) y solo los representa. Los montos vienen calculados;
/// Documents únicamente los formatea al renderizar.
/// </summary>
public sealed record GenerateInvoiceDocumentCommand(
    Guid TenantId,
    Guid InvoiceId,
    string InvoiceNumber,
    int DocumentVersion,
    string TemplateKey,
    int TemplateVersion,
    int TaxYear,
    string SourceService,
    string IdempotencyKey,
    string CorrelationId,
    InvoicePayload Invoice,
    BrandingPayload? Branding = null
);

/// <summary>
/// Configuración de marca del tenant aplicada a la plantilla (logo, color, pie, nombre visible). Todo
/// opcional: si no viene, la factura usa el look por defecto. El logo debe ser un data-URI embebido
/// (`data:image/...`) — un recurso externo lo bloquea el CSP del motor y no se imprimiría. Para la
/// "libertad total" (que el tenant escriba su propio HTML) está el sistema de plantillas versionadas
/// en BD (fase posterior); esto cubre el branding sin exponer edición de HTML.
/// </summary>
public sealed record BrandingPayload(
    string? DisplayName = null,
    string? LogoDataUri = null,
    string? BrandColorHex = null,
    string? FooterText = null
);

/// <summary>
/// Datos de la factura a representar. Inmutable; llega tal cual del emisor (Billing). Documents no
/// decide si está pagada ni fabrica el link de pago: <see cref="Status"/>, <see cref="PaidDate"/> y
/// <see cref="PaymentUrl"/> son DATO de Billing (que escucha los eventos de pago y es dueño de la
/// factura). La plantilla solo los representa: marca de agua según el estado, botón de pago si hay URL.
/// </summary>
public sealed record InvoicePayload(
    string Currency,
    DateOnly IssueDate,
    DateOnly? DueDate,
    InvoiceParty Issuer,
    InvoiceParty Customer,
    IReadOnlyList<InvoiceLine> Lines,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    string? Notes,
    // Estado de cobro que provee Billing. Presentación (watermark/botón) la decide la plantilla.
    string Status = "Pending",
    DateOnly? PaidDate = null,
    string? PaymentUrl = null,
    // Comprobante que provee Billing al pagar: número de recibo + hash de verificación (SHA-256).
    string? ReceiptNumber = null,
    string? ReceiptHash = null,
    // Onboarding con código: descuento total + una línea de ajuste por beneficio (referido/promo/gift).
    // SettlementType = "Paid" | "Mixed" | "FullyCoveredByCode" (total $0). Retrocompatible (defaults).
    decimal Discount = 0m,
    IReadOnlyList<InvoiceAdjustment>? Adjustments = null,
    string? SettlementType = null
);

public sealed record InvoiceParty(string Name, string TaxId, string? Address);

public sealed record InvoiceLine(string Description, decimal Quantity, decimal UnitPrice, decimal Amount);

/// <summary>Línea de ajuste (descuento) a representar. <see cref="Amount"/> es la magnitud positiva;
/// la plantilla la muestra en negativo.</summary>
public sealed record InvoiceAdjustment(string Label, decimal Amount);

/// <summary>Respuesta 202: la generación se registró; el archivo se produce de forma asíncrona.</summary>
public sealed record GenerateInvoiceDocumentResult(Guid GenerationId, string Status);
