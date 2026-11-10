namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio Calendar, usados como policy en <c>[HasPermission(...)]</c>.
///
/// <para>
/// Portal (D-CAL-H): el cliente ve sus citas con <see cref="PortalRead"/> y reserva, cancela o
/// reprograma las suyas con <see cref="PortalBook"/>.
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

    /// <summary>Portal: el cliente ve sus citas.</summary>
    public const string PortalRead = "calendar.portal.read";

    /// <summary>Portal: el cliente reserva, cancela y reprograma las suyas, según la política de la oficina.</summary>
    public const string PortalBook = "calendar.portal.book";
}
