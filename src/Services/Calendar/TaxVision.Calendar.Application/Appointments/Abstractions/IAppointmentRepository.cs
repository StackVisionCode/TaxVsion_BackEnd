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
    /// usando <see cref="GetByIdAsync"/> sin filtrar. Default = sin filtro (fakes) → el repo real lo sobrescribe.
    /// </summary>
    Task<Result<Appointment>> GetByIdForReadAsync(
        Guid tenantId,
        Guid appointmentId,
        Guid? assignedToUserId,
        CancellationToken ct = default
    ) => GetByIdAsync(tenantId, appointmentId, ct);

    /// <summary>
    /// Las puntuales del rango, por indice; y <b>todas</b> las series del tenant, que se expanden en
    /// memoria porque su <c>StartUtc</c> es NULL por diseno y ningun indice puede filtrarlas por fecha.
    /// Correcto mientras las series sean cientos: el umbral de revision escrito es 2.000 por tenant.
    /// </summary>
    Task<IReadOnlyList<Appointment>> ListForRangeAsync(
        Guid tenantId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        CancellationToken ct = default
    );

    /// <summary>
    /// Como <see cref="ListForRangeAsync"/> pero aplicando visibilidad por asignación (P2): si
    /// <paramref name="assignedToUserId"/> no es null, excluye las citas de clientes NO asignados al actor (las
    /// sin cliente y las que él organiza siguen visibles). Default = sin filtro (fakes/scheduling) → el repo real
    /// lo sobrescribe con el JOIN a la proyección. El conflicto/availability siguen usando la versión sin filtrar.
    /// </summary>
    Task<IReadOnlyList<Appointment>> ListVisibleForRangeAsync(
        Guid tenantId,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        Guid? assignedToUserId,
        CancellationToken ct = default
    ) => ListForRangeAsync(tenantId, rangeStartUtc, rangeEndUtc, ct);

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

    /// <summary>Cuántas citas vigentes organiza (mismo filtro que ListFutureByOrganizerAsync, sin
    /// materializar) — pre-flight de impacto al retirarlo. Default 0 para no romper los fakes; el repo
    /// real lo implementa con COUNT.</summary>
    Task<int> CountFutureByOrganizerAsync(
        Guid tenantId,
        Guid organizerUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) => Task.FromResult(0);

    void Add(Appointment appointment);

    void Remove(Appointment appointment);
}
