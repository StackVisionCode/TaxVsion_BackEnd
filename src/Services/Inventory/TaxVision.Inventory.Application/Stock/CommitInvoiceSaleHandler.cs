using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Domain;
using TaxVision.Inventory.Domain.Stock;

namespace TaxVision.Inventory.Application.Stock;

/// <summary>Una línea de producto vendida en una factura (referencia débil por CatalogItemId).</summary>
public sealed record CommitSaleLine(Guid CatalogItemId, int Quantity);

/// <summary>
/// Reconcilia el stock descontado por una factura al estado DESEADO (<see cref="Lines"/>): calcula, por
/// producto, lo ya descontado por esta factura (movimientos con la referencia = id de factura) y aplica
/// solo el delta — <b>Sale</b> si hay que descontar más, <b>Return</b> si hay que reponer. Así, la MISMA
/// operación cubre emitir (0 → cantidad), editar (cambia cantidades) y anular (cantidades vacías → repone
/// todo). Idempotente: reejecutar con el mismo deseo no mueve nada. <b>Bloqueante</b>: si algún producto
/// rastreado no alcanza para un incremento, no aplica NADA y devuelve <see cref="InventoryErrors.InsufficientStock"/>
/// (409). Servicios y productos sin nivel de stock se ignoran.
/// </summary>
public sealed record CommitInvoiceSaleCommand(Guid TenantId, Guid InvoiceId, IReadOnlyList<CommitSaleLine> Lines);

public static class CommitInvoiceSaleHandler
{
    public static async Task<Result> Handle(
        CommitInvoiceSaleCommand command,
        IStockRepository stock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Result.Failure(InventoryErrors.InvalidTenant);
        if (command.InvoiceId == Guid.Empty)
            return Result.Failure(InventoryErrors.InvalidCatalogItem);

        var reference = command.InvoiceId.ToString();

        // Estado DESEADO por ítem (cantidades de la factura tras esta operación; vacío ⇒ anulación).
        var desired = command
            .Lines.Where(l => l.CatalogItemId != Guid.Empty && l.Quantity > 0)
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        // Estado ACTUAL descontado por esta factura, del ledger: Sale suma, Return resta.
        var priorMovements = await stock.GetMovementsByReferenceAsync(command.TenantId, reference, ct);
        var current = new Dictionary<Guid, int>();
        foreach (var m in priorMovements)
        {
            var signed =
                m.Type == StockMovementType.Sale ? m.Quantity
                : m.Type == StockMovementType.Return ? -m.Quantity
                : 0;
            current[m.CatalogItemId] = current.GetValueOrDefault(m.CatalogItemId) + signed;
        }

        // Ítems a considerar: los deseados ∪ los ya movidos por esta factura (para poder reponer los que salieron).
        var itemIds = desired.Keys.Union(current.Keys).ToList();
        if (itemIds.Count == 0)
            return Result.Success();

        var levels = (await stock.GetByCatalogItemsAsync(command.TenantId, itemIds, ct)).ToDictionary(l =>
            l.CatalogItemId
        );

        // Verificación previa: cualquier INCREMENTO en un producto rastreado que no alcance ⇒ bloquear sin tocar nada.
        var shortfalls = new List<Guid>();
        foreach (var itemId in itemIds)
        {
            var delta = desired.GetValueOrDefault(itemId) - current.GetValueOrDefault(itemId);
            if (delta <= 0)
                continue; // Reposición o sin cambio: nunca bloquea.
            // Incremento: solo se descuenta de un nivel rastreado; si no alcanza, es faltante.
            if (levels.TryGetValue(itemId, out var lvl) && lvl.IsTracked && lvl.QuantityOnHand < delta)
                shortfalls.Add(itemId);
        }
        if (shortfalls.Count > 0)
            return Result.Failure(
                new Error(
                    InventoryErrors.InsufficientStock.Code,
                    $"Insufficient stock for {shortfalls.Count} product(s): {string.Join(", ", shortfalls)}."
                )
            );

        // Aplicar deltas. Sale para incrementos (solo rastreados), Return para reposiciones (siempre que haya nivel).
        var nowUtc = DateTime.UtcNow;
        var moved = false;
        foreach (var itemId in itemIds)
        {
            var delta = desired.GetValueOrDefault(itemId) - current.GetValueOrDefault(itemId);
            if (delta == 0)
                continue;
            if (!levels.TryGetValue(itemId, out var level))
                continue; // Servicio / producto sin nivel de stock: no se descuenta ni repone.
            if (delta > 0 && !level.IsTracked)
                continue; // No se descuenta de un ítem no rastreado (aunque sí se repondría).

            var type = delta > 0 ? StockMovementType.Sale : StockMovementType.Return;
            var qty = Math.Abs(delta);
            var move = level.RegisterMovement(type, qty, nowUtc);
            if (move.IsFailure)
                return Result.Failure(move.Error); // No debería pasar (ya se verificó), pero es fail-closed.

            await stock.AddMovementAsync(
                new StockMovement(
                    command.TenantId,
                    itemId,
                    type,
                    qty,
                    move.Value.Previous,
                    move.Value.New,
                    reference,
                    type == StockMovementType.Sale ? "Invoice sale" : "Invoice restock",
                    Guid.Empty, // Movimiento de sistema (factura), no de un usuario.
                    nowUtc
                ),
                ct
            );
            moved = true;
        }

        if (moved)
            await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
