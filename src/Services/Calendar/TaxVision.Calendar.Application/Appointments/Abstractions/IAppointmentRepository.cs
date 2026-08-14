using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Appointments.Abstractions;

public interface IAppointmentRepository
{
    Task<Result<Appointment>> GetByIdAsync(Guid tenantId, Guid appointmentId, CancellationToken ct = default);

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

    void Add(Appointment appointment);

    void Remove(Appointment appointment);
}
