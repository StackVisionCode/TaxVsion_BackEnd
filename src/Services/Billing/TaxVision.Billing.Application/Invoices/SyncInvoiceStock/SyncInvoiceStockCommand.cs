namespace TaxVision.Billing.Application.Invoices.SyncInvoiceStock;

/// <summary>Reconcilia en Inventory el stock de una factura con su estado actual. Viaja por la cola local
/// durable "billing-stock-sync" y se publica en la misma transacción que el cambio de la factura.</summary>
public sealed record SyncInvoiceStockCommand(Guid TenantId, Guid InvoiceId);
