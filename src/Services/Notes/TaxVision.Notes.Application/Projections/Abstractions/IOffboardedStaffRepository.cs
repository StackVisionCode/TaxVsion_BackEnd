using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.Abstractions;

/// <summary>
/// Puerto de la proyección de empleados retirados. Se ESCRIBE desde el consumer (scope Wolverine sin
/// tenant ambiente) y se LEE desde el hot path de autorización (scope HTTP) — la impl usa
/// <c>IgnoreQueryFilters()</c> + tenant explícito para funcionar en ambos.
/// </summary>
public interface IOffboardedStaffRepository
{
    Task<bool> IsOffboardedAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task<OffboardedStaffProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    Task AddAsync(OffboardedStaffProjection projection, CancellationToken ct = default);
}
