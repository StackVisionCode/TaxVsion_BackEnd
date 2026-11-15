using BuildingBlocks.TenantTimeZones;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Domain.Office;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

public sealed class OfficeScheduleRepository(CalendarDbContext context) : IOfficeScheduleRepository
{
    // Sin filtro de tenant: los consumers corren fuera de una petición.
    public Task<OfficeScheduleProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        context.OfficeSchedules.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task<string?> GetTimeZoneIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        // La proyección compartida es versionada y se reconcilia contra Tenant: manda sobre la fila del horario.
        var projected = await context
            .Set<TenantTimeZoneProjection>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(projected))
            return projected;
        return await context
            .OfficeSchedules.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(OfficeScheduleProjection projection) => context.OfficeSchedules.Add(projection);
}
