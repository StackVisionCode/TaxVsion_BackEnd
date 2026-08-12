namespace TaxVision.Inventory.Domain.Stock;

/// <summary>Tipo de movimiento del ledger de stock. Purchase/Return suman; Sale/Damaged restan;
/// Adjustment/Transfer llevan una cantidad con SIGNO (delta).</summary>
public enum StockMovementType
{
    Purchase,
    Sale,
    Adjustment,
    Return,
    Transfer,
    Damaged,
}
