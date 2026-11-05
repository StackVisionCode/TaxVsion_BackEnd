using BuildingBlocks.Results;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Domain;
using TaxVision.Inventory.Domain.Stock;

namespace TaxVision.Inventory.Application.Stock;

/// <summary>
/// Verificación de solo lectura antes de emitir o editar una factura: calcula el mismo delta que
/// <see cref="CommitInvoiceSaleHandler"/> (deseado menos lo ya descontado por la factura) y responde si el
/// stock alcanza. No toma candados ni escribe; el descuento real lo hace commit-sale después.
/// </summary>
public sealed record CheckInvoiceSaleQuery(Guid TenantId, Guid InvoiceId, IReadOnlyList<CommitSaleLine> Lines);

public static class CheckInvoiceSaleHandler
{
    public static async Task<Result> Handle(CheckInvoiceSaleQuery query, IStockRepository stock, CancellationToken ct)
    {
        if (query.TenantId == Guid.Empty)
            return Result.Failure(InventoryErrors.InvalidTenant);
        if (query.InvoiceId == Guid.Empty)
            return Result.Failure(InventoryErrors.InvalidCatalogItem);

        var desired = query
            .Lines.Where(l => l.CatalogItemId != Guid.Empty && l.Quantity > 0)
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (desired.Count == 0)
            return Result.Success();

        var current = new Dictionary<Guid, int>();
        foreach (var m in await stock.GetMovementsByReferenceAsync(query.TenantId, query.InvoiceId.ToString(), ct))
        {
            var signed =
                m.Type == StockMovementType.Sale ? m.Quantity
                : m.Type == StockMovementType.Return ? -m.Quantity
                : 0;
            current[m.CatalogItemId] = current.GetValueOrDefault(m.CatalogItemId) + signed;
        }

        var levels = (await stock.GetByCatalogItemsAsync(query.TenantId, desired.Keys.ToList(), ct)).ToDictionary(l =>
            l.CatalogItemId
        );

        // Solo un incremento sobre un producto rastreado puede faltar; reponer nunca bloquea.
        var shortfalls = desired
            .Where(d =>
                d.Value - current.GetValueOrDefault(d.Key) is var delta and > 0
                && levels.TryGetValue(d.Key, out var lvl)
                && lvl.IsTracked
                && lvl.QuantityOnHand < delta
            )
            .Select(d => d.Key)
            .ToList();

        return shortfalls.Count == 0
            ? Result.Success()
            : Result.Failure(
                new Error(
                    InventoryErrors.InsufficientStock.Code,
                    $"Insufficient stock for {shortfalls.Count} product(s): {string.Join(", ", shortfalls)}."
                )
            );
    }
}
