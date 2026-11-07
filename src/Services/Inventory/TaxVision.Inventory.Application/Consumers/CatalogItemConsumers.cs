using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CatalogIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Domain.Stock;

namespace TaxVision.Inventory.Application.Consumers;

// Inventory reacciona al catálogo por eventos (referencia débil por CatalogItemId, sin llamar a Catalog).
// Wolverine descubre los métodos estáticos `Handle`; se registran EXPLÍCITO en Program.cs (IncludeType).
// Leen el nivel con candado: el primer AdjustStock del ítem también lo puede estar creando (INV-5).

public static class CatalogItemCreatedConsumer
{
    public static async Task Handle(
        CatalogItemCreatedIntegrationEvent evt,
        IStockRepository stock,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<StockLevel> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            // Solo se abre un nivel de stock para ítems que declaran rastreo (productos con TrackInventory).
            if (!evt.TrackInventory)
                return;

            var existing = await stock.GetByCatalogItemForUpdateAsync(evt.TenantId, evt.ItemId, ct);
            if (existing is not null)
                return;

            var created = StockLevel.CreateFromCatalog(evt.TenantId, evt.ItemId, true, evt.OccurredOn, DateTime.UtcNow);
            if (created.IsFailure)
                return;

            await stock.AddStockLevelAsync(created.Value, ct);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "StockLevel opened for catalog item {ItemId} (tenant {TenantId}).",
                evt.ItemId,
                evt.TenantId
            );
        }
    }
}

/// <summary>
/// Reactivación o cambio de rastreo. Al volver a rastrear no se recalcula <c>QuantityOnHand</c>: mientras no se
/// rastreó, las facturas no descontaron, y ajustar es decisión de la oficina (conteo físico).
/// </summary>
public static class CatalogItemTrackingChangedConsumer
{
    public static async Task Handle(
        CatalogItemTrackingChangedIntegrationEvent evt,
        IStockRepository stock,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<StockLevel> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var shouldTrack = evt.TrackInventory && evt.IsActive;
            var level = await stock.GetByCatalogItemForUpdateAsync(evt.TenantId, evt.ItemId, ct);

            if (level is null)
            {
                if (!shouldTrack)
                    return;

                var created = StockLevel.CreateFromCatalog(
                    evt.TenantId,
                    evt.ItemId,
                    true,
                    evt.OccurredOn,
                    DateTime.UtcNow
                );
                if (created.IsFailure)
                    return;

                await stock.AddStockLevelAsync(created.Value, ct);
            }
            else if (!level.ApplyCatalogTracking(shouldTrack, evt.OccurredOn, DateTime.UtcNow))
            {
                logger.LogInformation("Out-of-order catalog event ignored for item {ItemId}.", evt.ItemId);
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "StockLevel tracking of catalog item {ItemId} set to {Tracked}.",
                evt.ItemId,
                shouldTrack
            );
        }
    }
}

public static class CatalogItemDeactivatedConsumer
{
    public static async Task Handle(
        CatalogItemDeactivatedIntegrationEvent evt,
        IStockRepository stock,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<StockLevel> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var level = await stock.GetByCatalogItemForUpdateAsync(evt.TenantId, evt.ItemId, ct);
            if (level is null)
            {
                // Evento viejo o ítem sin rastreo: no hay nada que marcar.
                if (evt.TrackInventory != true)
                    return;

                // El Created aún no llega: esta marca hace que no abra un nivel rastreado para un ítem desactivado.
                var tombstone = StockLevel.CreateFromCatalog(
                    evt.TenantId,
                    evt.ItemId,
                    false,
                    evt.OccurredOn,
                    DateTime.UtcNow
                );
                if (tombstone.IsFailure)
                    return;

                await stock.AddStockLevelAsync(tombstone.Value, ct);
            }
            else if (!level.ApplyCatalogTracking(false, evt.OccurredOn, DateTime.UtcNow))
            {
                logger.LogInformation("Out-of-order catalog event ignored for item {ItemId}.", evt.ItemId);
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("StockLevel untracked for deactivated catalog item {ItemId}.", evt.ItemId);
        }
    }
}
