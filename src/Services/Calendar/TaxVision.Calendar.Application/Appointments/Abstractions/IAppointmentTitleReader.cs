using BuildingBlocks.Contracts.Work;

namespace TaxVision.Calendar.Application.Appointments.Abstractions;

/// <summary>
/// Títulos en vivo de citas (Activity) en una sola consulta, con la visibilidad por asignación del detalle:
/// <paramref name="assignedToUserId"/> null = sin restricción. Lo de otra oficina o que no existe no vuelve.
/// </summary>
public interface IAppointmentTitleReader
{
    Task<IReadOnlyList<WorkTitle>> ListTitlesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> appointmentIds,
        Guid? assignedToUserId,
        CancellationToken ct = default
    );
}
