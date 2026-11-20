namespace TaxVision.PaymentClient.Application.Payables.EnsureInvoicePayable;

/// <summary>Asegura (find-or-create) el ancla estable de cobro de una factura. Idempotente por
/// (TenantId, InvoicePayment, InvoiceId): reintentar devuelve el mismo payable/referencia.</summary>
public sealed record EnsureInvoicePayableCommand(
    Guid TenantId,
    long AmountCents,
    string Currency,
    string InvoiceId,
    string? Description = null,
    // Solo cuando Billing lo pide EXPLÍCITAMENTE (la oficina corrigió el estado y volvió a dejar cobrable una factura
    // anulada o devuelta a borrador): un payable revocado se reinstala. Sin esto un «asegurar» tardío no puede
    // resucitar una factura anulada.
    bool Reinstate = false
);

public sealed record EnsureInvoicePayableResponse(Guid PayableId, string Reference, string? SubDomain);
