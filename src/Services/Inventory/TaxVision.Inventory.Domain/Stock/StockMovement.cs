using BuildingBlocks.Domain;

namespace TaxVision.Inventory.Domain.Stock;

/// <summary>Fila inmutable del ledger de stock — un intento de movimiento, con la cantidad previa y
/// nueva congeladas para auditoría.</summary>
public sealed class StockMovement : TenantEntity
{
    public Guid CatalogItemId { get; private set; }
    public StockMovementType Type { get; private set; }
    public int Quantity { get; private set; }
    public int PreviousQuantity { get; private set; }
    public int NewQuantity { get; private set; }
    public string? Reference { get; private set; }
    public string? Notes { get; private set; }
    public Guid MovedByUserId { get; private set; }
    public DateTime MovedAtUtc { get; private set; }

    private StockMovement() { }

    public StockMovement(
        Guid tenantId,
        Guid catalogItemId,
        StockMovementType type,
        int quantity,
        int previousQuantity,
        int newQuantity,
        string? reference,
        string? notes,
        Guid movedByUserId,
        DateTime movedAtUtc
    )
    {
        CatalogItemId = catalogItemId;
        Type = type;
        Quantity = quantity;
        PreviousQuantity = previousQuantity;
        NewQuantity = newQuantity;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        MovedByUserId = movedByUserId;
        MovedAtUtc = movedAtUtc;
        SetTenant(tenantId);
    }
}
