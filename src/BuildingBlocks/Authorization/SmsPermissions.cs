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

    /// <summary>
    /// Leer el historial de SMS del tenant y las bajas (opt-outs): lista paginada de mensajes con su
    /// estado, detalle, stats agregadas y la lista de opt-outs. Lo exigen los endpoints GET de lectura
    /// del CRM. Lo reciben TenantAdmin y TenantEmployee (trabajo operativo diario).
    /// </summary>
    public const string Read = "sms.read";

    /// <summary>
    /// Gestionar manualmente el consentimiento de un cliente (dar de baja/alta un teléfono sin esperar
    /// al STOP/START entrante). Operación de administración del tenant → solo TenantAdmin.
    /// </summary>
    public const string Manage = "sms.manage";
}
