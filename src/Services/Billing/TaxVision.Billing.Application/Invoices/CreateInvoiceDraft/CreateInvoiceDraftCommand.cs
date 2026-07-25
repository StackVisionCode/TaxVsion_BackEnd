namespace TaxVision.Billing.Application.Invoices.CreateInvoiceDraft;

/// <summary>SCAFFOLD B1: contrato mínimo del comando de creación de borrador. Los campos completos
/// (líneas, descuento, cliente, fechas) se detallan en B2. Ver
/// documents/architecture/billing/07_Invoices_Use_Case_Catalog.md (UC-01).</summary>
public sealed record CreateInvoiceDraftCommand(Guid TenantId, Guid ActorUserId, string IdempotencyKey);
