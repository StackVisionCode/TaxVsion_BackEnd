using BuildingBlocks.Results;

namespace TaxVision.Billing.Application.Abstractions;

/// <summary>
/// Puerto M2M hacia el servicio Documents: pide generar el PDF de una factura. La generación es
/// asíncrona (Documents responde 202); el FileId llega después vía documents.generation.completed.
/// Montos en decimales (dólares) porque ese es el contrato de Documents — Billing convierte desde
/// sus centavos antes de llamar.
/// </summary>
public interface IInvoiceDocumentClient
{
    Task<Result> GenerateAsync(
        InvoiceDocumentRequest request,
        Guid tenantId,
        string idempotencyKey,
        string? correlationId,
        CancellationToken ct = default
    );
}

public sealed record InvoiceDocumentRequest(
    Guid InvoiceId,
    string InvoiceNumber,
    int TaxYear,
    string Currency,
    DateOnly IssueDate,
    DateOnly? DueDate,
    InvoiceDocParty Issuer,
    InvoiceDocParty Customer,
    IReadOnlyList<InvoiceDocLine> Lines,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    string? Notes,
    string Status,
    string? PaymentUrl,
    DateOnly? PaidDate = null,
    string? ReceiptNumber = null,
    string? ReceiptHash = null,
    // Onboarding con código: descuento total + una línea de ajuste (negativa) por beneficio aplicado.
    // SettlementType distingue "Paid" / "Mixed" / "FullyCoveredByCode" (total $0). Retrocompatible.
    decimal Discount = 0m,
    IReadOnlyList<InvoiceDocAdjustment>? Adjustments = null,
    string? SettlementType = null
);

public sealed record InvoiceDocParty(string Name, string TaxId, string? Address);

public sealed record InvoiceDocLine(string Description, decimal Quantity, decimal UnitPrice, decimal Amount);

/// <summary>Línea de ajuste (descuento) para el PDF. <see cref="Amount"/> es la magnitud positiva del
/// descuento; la plantilla la muestra en negativo.</summary>
public sealed record InvoiceDocAdjustment(string Label, decimal Amount);
