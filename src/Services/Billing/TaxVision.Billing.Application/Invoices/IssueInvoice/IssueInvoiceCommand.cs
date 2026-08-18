namespace TaxVision.Billing.Application.Invoices.IssueInvoice;

/// <summary>Emite un borrador: le asigna el número server-side + fechas y lo pasa a Issued, y dispara
/// la generación del PDF en Documents (async).</summary>
public sealed record IssueInvoiceCommand(Guid TenantId, Guid InvoiceId, Guid ActorUserId);

public sealed record IssueInvoiceResult(Guid InvoiceId, string InvoiceNumber, string Status);
