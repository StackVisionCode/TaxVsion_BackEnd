namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio Catalog (productos/servicios/categorías). Mismo patrón que
/// <see cref="SmsPermissions"/>: claves punteadas usadas como claim "perm" en el JWT y como policy en
/// los endpoints (<c>[HasPermission(...)]</c>).
/// </summary>
public static class CatalogPermissions
{
    /// <summary>Ver/listar ítems y categorías del catálogo.</summary>
    public const string Read = "catalog.read";

    /// <summary>Crear/editar ítems y categorías (incluye cambio de precio y activar/desactivar).</summary>
    public const string Write = "catalog.write";

    /// <summary>Borrar (soft-delete) ítems y categorías.</summary>
    public const string Delete = "catalog.delete";
}
