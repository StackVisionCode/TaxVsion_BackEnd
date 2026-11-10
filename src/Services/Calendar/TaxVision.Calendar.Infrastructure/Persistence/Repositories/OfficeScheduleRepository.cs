using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Domain.Office;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

public sealed class OfficeScheduleRepository(CalendarDbContext context) : IOfficeScheduleRepository
{
    // Sin filtro de tenant: los consumers corren fuera de una petición.
    public Task<OfficeScheduleProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        context.OfficeSchedules.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public void Add(OfficeScheduleProjection projection) => context.OfficeSchedules.Add(projection);
}
