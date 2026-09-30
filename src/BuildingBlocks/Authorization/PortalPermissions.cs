namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del portal del cliente final. Viven acá —y no solo en el catálogo de Auth— porque
/// los aplica el servicio dueño del endpoint (CloudStorage, por ejemplo), que no referencia Auth.
/// </summary>
public static class PortalPermissions
{
    public const string CallsUse = "portal.calls.use";
    public const string MilesUse = "portal.miles.use";
    public const string FoldersView = "portal.folders.view";
}
