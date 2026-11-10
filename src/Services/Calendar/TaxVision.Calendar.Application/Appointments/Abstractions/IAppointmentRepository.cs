using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Appointments.Abstractions;

public interface IAppointmentRepository
{
    Task<Result<Appointment>> GetByIdAsync(Guid tenantId, Guid appointmentId, CancellationToken ct = default);

    /// <summary>
    /// Como <see cref="GetByIdAsync"/> pero aplicando visibilidad por asignación (P2) en el read del detalle:
    /// si <paramref name="assignedToUserId"/> no es null, devuelve NotFound cuando la cita es de un cliente NO
    /// asignado al actor (las citas sin cliente y las que él organiza siguen visibles). El path de MUTACIÓN sigue
    /// usando <see cref="GetByIdAsync"/> sin filtrar.
    /// </summary>
    Task<Result<Appointment>> GetByIdForReadAsync(
        Guid tenantId,
        Guid appointmentId,
        Guid? assignedToUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Las puntuales del rango, por indice; y <b>todas</b> las series del tenant, que se expanden en
    /// memoria porque su <c>StartUtc</c> es NULL por diseno y ningun indice puede filtrarlas por fecha.
    /// Correcto mientras las series sean cientos: el umbral de revision escrito es 2.000 por tenant.
    /// </summary>
    /// <summary>
    /// Las citas de un organizador y un tipo que pueden caer en el rango (series incluidas), para el tope
    /// diario.
    /// </summary>
    Task<IReadOnlyList<Appointment>> ListOrganizerTypeForRangeAsync(
        Guid tenantId,
        Guid organizerUserId,
        Guid appointmentTypeId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        CancellationToken ct = default
    );

    Task<IReadOnlyList<Appointment>> ListForRangeAsync(
        Guid tenantId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        CancellationToken ct = default
    );

    /// <summary>
    /// Como <see cref="ListForRangeAsync"/> pero aplicando visibilidad por asignación (P2): si
    /// <paramref name="assignedToUserId"/> no es null, excluye las citas de clientes NO asignados al actor (las
    /// sin cliente y las que él organiza siguen visibles). El conflicto/availability siguen usando la versión sin filtrar.
    /// </summary>
    Task<IReadOnlyList<Appointment>> ListVisibleForRangeAsync(
        Guid tenantId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        Guid? assignedToUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Lo mismo, acotado a las citas de un usuario: las que organiza y aquellas a las que lo invitaron.
    /// Es la consulta del feed, y no puede reusar la de rango porque esa devuelve la agenda del tenant
    /// entero.
    /// </summary>
    Task<IReadOnlyList<Appointment>> ListForUserRangeAsync(
        Guid tenantId,
        Guid userId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        CancellationToken ct = default
    );

    /// <summary>
    /// Citas ACTIVAS (no canceladas) que ORGANIZA <paramref name="organizerUserId"/> y siguen vigentes:
    /// las puntuales cuyo fin es posterior a <paramref name="nowUtc"/>, más TODAS las series (su
    /// <c>StartUtc</c> es NULL por diseño, no se puede filtrar por fecha en SQL). Para reasignar o
    /// cancelar su trabajo al retirarlo (offboard). Tracked (se mutan). Index-backed por (TenantId, OrganizerUserId, Status).
    /// </summary>
    Task<IReadOnlyList<Appointment>> ListFutureByOrganizerAsync(
        Guid tenantId,
        Guid organizerUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    /// <summary>
    /// Citas ACTIVAS y vigentes (mismo corte que <see cref="ListFutureByOrganizerAsync"/>) donde
    /// <paramref name="userId"/> figura como asistente interno, para sacarlo al retirarlo (offboard).
    /// Tracked (se mutan). Index-backed por (UserId, AppointmentId) de los asistentes.
    /// </summary>
    Task<IReadOnlyList<Appointment>> ListFutureByStaffAttendeeAsync(
        Guid tenantId,
        Guid userId,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    /// <summary>Cuántas citas vigentes organiza (mismo filtro que ListFutureByOrganizerAsync, sin
    /// materializar) — pre-flight de impacto al retirarlo.</summary>
    Task<int> CountFutureByOrganizerAsync(
        Guid tenantId,
        Guid organizerUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    void Add(Appointment appointment);

    void Remove(Appointment appointment);

    /// <summary>Olvida lo pendiente. El middleware guarda lo rastreado aunque el handler devuelva un fallo.</summary>
    void DiscardPendingChanges();
}
