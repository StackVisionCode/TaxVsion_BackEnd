namespace TaxVision.Billing.Application.Invoices.GenerateInvoicePdf;

/// <summary>Comando local post-commit: pide a Documents el PDF de una factura ya emitida. Corre en
/// su propia transacción (outbox), así la factura ya está persistida y visible.</summary>
public sealed record GenerateInvoicePdfCommand(Guid TenantId, Guid InvoiceId);
