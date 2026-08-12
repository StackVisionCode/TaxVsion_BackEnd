namespace TaxVision.Catalog.Domain.Items;

/// <summary>Tipo de ítem del catálogo. Un <see cref="Service"/> nunca lleva stock (TrackInventory=false);
/// un <see cref="Product"/> puede o no ser rastreado por el servicio Inventory (separado).</summary>
public enum ItemKind
{
    Product,
    Service,
}
