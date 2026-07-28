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
    string? ReceiptHash = null
);

public sealed record InvoiceDocParty(string Name, string TaxId, string? Address);

public sealed record InvoiceDocLine(string Description, decimal Quantity, decimal UnitPrice, decimal Amount);
