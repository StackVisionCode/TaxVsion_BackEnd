namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio Reminder. Mismo patrón que <see cref="NotesPermissions"/>: claves
/// punteadas en minúsculas usadas como policy en los endpoints (<c>[HasPermission(...)]</c>) y
/// resueltas contra la proyección local de permisos (RBAC Fase 7 — el claim <c>perm</c> ya no se
/// emite en tokens humanos).
///
/// <para>
/// <b>Sin permiso de portal.</b> Un recordatorio es siempre de un usuario del tenant
/// (<c>Reminder.UserId</c>, invariante R1) — el cliente final no tiene recordatorios propios en v1,
/// así que no existe un <c>reminders.portal_*</c> y la inferencia de <c>AllowedActorTypes</c> deja
/// fuera a <c>CustomerPortal</c>.
/// </para>
/// </summary>
public static class ReminderPermissions
{
    /// <summary>Listar y consultar los recordatorios propios.</summary>
    public const string Read = "reminders.read";

    /// <summary>
    /// Crear, reprogramar, posponer, descartar y cancelar recordatorios. No hay permiso separado
    /// de gobernanza: nadie edita recordatorios ajenos — el filtro por <c>UserId</c> lo aplica el
    /// handler, igual que hace Notes con la autoría.
    /// </summary>
    public const string Write = "reminders.write";
}
