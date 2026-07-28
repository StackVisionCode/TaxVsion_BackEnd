namespace TaxVision.PaymentClient.Application.Payables.EnsureInvoicePayable;

/// <summary>Asegura (find-or-create) el ancla estable de cobro de una factura. Idempotente por
/// (TenantId, InvoicePayment, InvoiceId): reintentar devuelve el mismo payable/referencia.</summary>
public sealed record EnsureInvoicePayableCommand(
    Guid TenantId,
    long AmountCents,
    string Currency,
    string InvoiceId
);

public sealed record EnsureInvoicePayableResponse(Guid PayableId, string Reference);
