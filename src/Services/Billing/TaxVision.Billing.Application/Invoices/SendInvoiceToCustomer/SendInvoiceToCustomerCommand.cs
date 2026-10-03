namespace TaxVision.Billing.Application.Invoices.SendInvoiceToCustomer;

/// <summary>Manda la factura por correo al cliente. Billing publica el evento; Notification y Scribe
/// se encargan del correo.</summary>
public sealed record SendInvoiceToCustomerCommand(Guid TenantId, Guid InvoiceId, Guid ActorUserId, bool CanViewAll);
