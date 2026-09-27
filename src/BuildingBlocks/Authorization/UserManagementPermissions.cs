namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos de gestion de usuarios del tenant. Viven en Auth (PermissionCatalog los alias), pero
/// otros servicios los necesitan para gatear lo que solo tiene sentido dentro del flujo de retiro
/// de un empleado: la vista previa de impacto que cada servicio expone en
/// <c>GET .../offboarding-impact/{userId}</c>.
/// </summary>
public static class UserManagementPermissions
{
    public const string UsersManage = "users.manage";
}
