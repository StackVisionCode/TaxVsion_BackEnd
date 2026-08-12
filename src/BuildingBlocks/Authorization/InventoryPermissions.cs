namespace BuildingBlocks.Authorization;

/// <summary>Permisos del microservicio Inventory (stock, proveedores, movimientos). Mismo patrón que
/// <see cref="CatalogPermissions"/>.</summary>
public static class InventoryPermissions
{
    /// <summary>Ver stock, proveedores y movimientos.</summary>
    public const string Read = "inventory.read";

    /// <summary>Gestionar proveedores, vínculos ítem-proveedor y umbrales de stock.</summary>
    public const string Write = "inventory.write";

    /// <summary>Ajustar stock (registrar un movimiento en el ledger).</summary>
    public const string Adjust = "inventory.adjust";
}
