namespace TaxVision.Billing.Application.Invoices.EnsureInvoicePaymentLink;

/// <summary>Asegura el ancla estable de cobro de una factura emitida (vía PaymentClient) y luego
/// dispara la generación del PDF. Paso propio del pipeline (outbox) con retry independiente — punto 7
/// del review: una caída de PaymentClient no bloquea toda la generación documental de golpe.</summary>
public sealed record EnsureInvoicePaymentLinkCommand(Guid TenantId, Guid InvoiceId);
