using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Office.TimeZoneChanges;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Office;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Infrastructure.Persistence.Repositories;

public sealed class OfficeTimeZoneChangeRepository(CalendarDbContext context) : IOfficeTimeZoneChangeRepository
{
    public void Add(OfficeTimeZoneChange change) => context.OfficeTimeZoneChanges.Add(change);

    public Task<OfficeTimeZoneChange?> GetPendingAsync(Guid tenantId, CancellationToken ct = default) =>
        context
            .OfficeTimeZoneChanges.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Status == OfficeTimeZoneChangeStatus.Pending)
            .OrderByDescending(c => c.TenantVersion)
            .FirstOrDefaultAsync(ct);

    public Task<bool> ExistsForVersionAsync(Guid tenantId, long tenantVersion, CancellationToken ct = default) =>
        context
            .OfficeTimeZoneChanges.IgnoreQueryFilters()
            .AnyAsync(c => c.TenantId == tenantId && c.TenantVersion == tenantVersion, ct);
}

public sealed class OfficeTimeZoneCandidates(CalendarDbContext context) : IOfficeTimeZoneCandidates
{
    public async Task<IReadOnlyList<AvailabilityRule>> ActiveRulesInZoneAsync(
        Guid tenantId,
        string timeZoneId,
        CancellationToken ct = default
    )
    {
        var zone = CalendarTimeZone.Create(timeZoneId).Value;
        return await context
            .AvailabilityRules.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.IsActive && r.TimeZone == zone)
            .OrderBy(r => r.UserId)
            .ThenBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Appointment>> LiveSeriesInZoneAsync(
        Guid tenantId,
        string timeZoneId,
        CancellationToken ct = default
    )
    {
        var zone = CalendarTimeZone.Create(timeZoneId).Value;
        return await context
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Exceptions)
            .Where(a =>
                a.TenantId == tenantId
                && a.Recurrence != null
                && a.Status != AppointmentStatus.Cancelled
                && a.Timing.TimeZone == zone
            )
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);
    }
}
