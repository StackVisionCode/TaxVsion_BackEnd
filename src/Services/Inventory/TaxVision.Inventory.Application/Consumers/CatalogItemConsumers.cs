using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CatalogIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Domain.Stock;

namespace TaxVision.Inventory.Application.Consumers;

// Inventory reacciona al catálogo por eventos (referencia débil por CatalogItemId, sin llamar a Catalog).
// Wolverine descubre los métodos estáticos `Handle`; se registran EXPLÍCITO en Program.cs (IncludeType).

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
        using (correlation.Push(string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId))
        {
            // Solo se abre un nivel de stock para ítems que declaran rastreo (productos con TrackInventory).
            if (!evt.TrackInventory)
                return;

            var existing = await stock.GetByCatalogItemAsync(evt.TenantId, evt.ItemId, ct);
            if (existing is not null)
                return;

            var created = StockLevel.Create(evt.TenantId, evt.ItemId, 0, 0, 0, 0, DateTime.UtcNow);
            if (created.IsFailure)
                return;

            await stock.AddStockLevelAsync(created.Value, ct);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("StockLevel opened for catalog item {ItemId} (tenant {TenantId}).", evt.ItemId, evt.TenantId);
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
        using (correlation.Push(string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId))
        {
            var level = await stock.GetByCatalogItemAsync(evt.TenantId, evt.ItemId, ct);
            if (level is null)
                return;

            level.SetTracked(false, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("StockLevel untracked for deactivated catalog item {ItemId}.", evt.ItemId);
        }
    }
}
