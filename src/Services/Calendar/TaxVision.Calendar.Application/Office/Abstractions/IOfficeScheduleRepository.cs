using TaxVision.Calendar.Domain.Office;

namespace TaxVision.Calendar.Application.Office.Abstractions;

public interface IOfficeScheduleRepository
{
    /// <summary>La proyección de la oficina, con tracking; null si todavía no llegó ningún evento.</summary>
    Task<OfficeScheduleProjection?> GetAsync(Guid tenantId, CancellationToken ct = default);

    void Add(OfficeScheduleProjection projection);
}
