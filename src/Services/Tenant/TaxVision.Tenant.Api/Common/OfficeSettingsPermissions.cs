namespace TaxVision.Tenant.Api.Common;

/// <summary>
/// Permiso del catálogo de Auth (<c>PermissionCatalog.SettingsManage</c>, "Manage office settings")
/// que exige editar el nombre y la zona horaria de la oficina. BuildingBlocks no lo expone como
/// constante, así que se refleja acá con el mismo código; el catálogo de Auth sigue siendo la fuente.
/// </summary>
public static class OfficeSettingsPermissions
{
    public const string Manage = "settings.manage";
}
