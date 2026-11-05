using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Application.Invoices.SyncInvoiceStock;

/// <summary>
/// Lleva el stock de Inventory al estado actual de la factura con commit-sale (idempotente por factura,
/// reconcilia por delta). Emitida ⇒ sus líneas de catálogo; borrador, anulada o inexistente ⇒ []. Un fallo
/// transitorio lanza para reintentar. Si falta stock, la factura ya está emitida: se registra y alerta.
/// </summary>
public static class SyncInvoiceStockHandler
{
    public const string InsufficientStockCode = "inventory.insufficientStock";

    public static async Task Handle(
        SyncInvoiceStockCommand command,
        IInvoiceRepository invoices,
        IInventoryStockClient inventory,
        IUnitOfWork unitOfWork,
        IBillingMetrics metrics,
        ILogger<SyncInvoiceStockCommand> logger,
        CancellationToken ct
    )
    {
        var invoice = await invoices.GetByIdAsync(command.TenantId, command.InvoiceId, ct);
        var holdsStock =
            invoice is { DeletedAtUtc: null }
            && invoice.Status is InvoiceStatus.Issued or InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid;

        IReadOnlyList<InvoiceSaleLine> lines = holdsStock
            ? invoice!
                .Lines.Where(l => l.CatalogItemId is { } cid && cid != Guid.Empty && l.Quantity > 0)
                .Select(l => new InvoiceSaleLine(l.CatalogItemId!.Value, l.Quantity))
                .ToList()
            : [];

        var committed = await inventory.CommitInvoiceSaleAsync(command.TenantId, command.InvoiceId, lines, ct);
        if (committed.IsFailure && committed.Error.Code != InsufficientStockCode)
            throw new InvalidOperationException(
                $"Inventory stock sync failed: {committed.Error.Code} - {committed.Error.Message}"
            );

        if (committed.IsFailure)
        {
            // No se puede des-emitir: queda el faltante para que la oficina lo resuelva. No se reintenta.
            metrics.RecordStockSyncInsufficient();
            logger.LogError(
                "Stock sync for invoice {InvoiceId} rejected: {Message}",
                command.InvoiceId,
                committed.Error.Message
            );
        }

        // Versión reconciliada (también con faltante: la alerta ya salió y el job no debe repetirla).
        if (invoice is not null)
        {
            invoice.MarkStockSynced(invoice.UpdatedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
