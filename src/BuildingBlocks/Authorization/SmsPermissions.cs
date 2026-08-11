namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio SMS (envío de SMS/MMS agnóstico de proveedor). Mismo patrón que
/// <see cref="ScribePermissions"/>: claves punteadas en minúsculas usadas como claim "perm" en el
/// JWT y como policy en los endpoints (<c>[HasPermission(...)]</c>).
/// </summary>
public static class SmsPermissions
{
    /// <summary>
    /// Enviar SMS/MMS (batch 1..N) vía "POST /sms/messages". Lo exigen tanto los callers M2M
    /// (microservicios que envían SMS vía token de servicio — el token debe llevar este código en
    /// sus Permissions configurados en ServiceAuth:Clients de Auth) como los usuarios de tenant
    /// (TenantAdmin/TenantEmployee) que lo reciben vía SystemRoleDefaults.
    /// </summary>
    public const string Send = "sms.send";
}
