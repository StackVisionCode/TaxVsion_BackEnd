using TaxVision.Calendar.Domain.Office;

namespace TaxVision.Calendar.Application.Office.Abstractions;

public interface IOfficeScheduleRepository
{
    /// <summary>La proyección de la oficina, con tracking; null si todavía no llegó ningún evento.</summary>
    Task<OfficeScheduleProjection?> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Zona de la oficina: la proyectada desde Tenant y, si falta, la de la fila del horario. Null si no se conoce.</summary>
    Task<string?> GetTimeZoneIdAsync(Guid tenantId, CancellationToken ct = default);

    void Add(OfficeScheduleProjection projection);
}
