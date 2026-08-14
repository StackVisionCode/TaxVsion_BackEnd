namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio Calendar, usados como policy en <c>[HasPermission(...)]</c>.
///
/// <para>
/// Sin permiso de portal: en v1 el cliente no entra al calendario. Lo que ve es la invitación que le
/// manda Notification y, si se suscribe, el feed <c>.ics</c> — que se autoriza por token firmado y no
/// por permiso.
/// </para>
/// </summary>
public static class CalendarPermissions
{
    /// <summary>Ver el calendario del tenant y consultar disponibilidad.</summary>
    public const string Read = "calendar.read";

    /// <summary>Crear, mover y cancelar las citas propias. Ser el organizador es cosa aparte.</summary>
    public const string Write = "calendar.write";

    /// <summary>
    /// Override de supervisión: reorganizar agendas ajenas. <b>No anula ADR-C-09</b> — el agregado
    /// sigue exigiendo organizador, y esto sólo permite al admin actuar como tal, dejando rastro.
    /// </summary>
    public const string ManageAll = "calendar.manage_all";

    /// <summary>Definir los tipos de cita de la firma: duración, color, si bloquea solapamiento.</summary>
    public const string TypesManage = "calendar.types.manage";

    /// <summary>Definir horarios de atención y bloqueos de agenda.</summary>
    public const string AvailabilityManage = "calendar.availability.manage";
}
