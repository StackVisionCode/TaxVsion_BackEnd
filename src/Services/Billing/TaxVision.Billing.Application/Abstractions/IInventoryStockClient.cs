using BuildingBlocks.Results;

namespace TaxVision.Billing.Application.Abstractions;

/// <summary>
/// Puerto M2M hacia Inventory: al EMITIR una factura, descuenta el stock de sus líneas de producto.
/// Bloqueante — si Inventory responde que falta stock (o no se puede verificar), el commit falla y la
/// factura NO se emite. Servicios y productos sin rastreo los ignora Inventory. Idempotente por factura.
/// </summary>
public interface IInventoryStockClient
{
    Task<Result> CommitInvoiceSaleAsync(
        Guid tenantId,
        Guid invoiceId,
        IReadOnlyList<InvoiceSaleLine> lines,
        CancellationToken ct = default
    );
}

/// <summary>Una línea de producto de la factura enviada a Inventory (referencia débil por CatalogItemId).</summary>
public sealed record InvoiceSaleLine(Guid CatalogItemId, int Quantity);
