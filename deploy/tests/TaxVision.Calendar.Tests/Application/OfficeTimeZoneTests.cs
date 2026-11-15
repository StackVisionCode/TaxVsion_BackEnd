using BuildingBlocks.Tenancy;
using BuildingBlocks.TenantTimeZones;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Domain.Office;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>La zona de la oficina sale de la proyección de Tenant; la fila del horario queda de respaldo.</summary>
public sealed class OfficeTimeZoneTests
{
    private const string NewYork = "America/New_York";
    private const string LaPaz = "America/La_Paz";

    [Fact]
    public async Task Repository_prefers_the_tenant_projection_and_falls_back_to_the_office_row()
    {
        var both = Guid.NewGuid();
        var rowOnly = Guid.NewGuid();
        await using var db = new CalendarDbContext(
            new DbContextOptionsBuilder<CalendarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        var stale = OfficeScheduleProjection.Create(both, DateTime.UtcNow);
        stale.ApplyTimeZone(LaPaz, DateTime.UtcNow, DateTime.UtcNow);
        var onlyRow = OfficeScheduleProjection.Create(rowOnly, DateTime.UtcNow);
        onlyRow.ApplyTimeZone(LaPaz, DateTime.UtcNow, DateTime.UtcNow);
        db.OfficeSchedules.AddRange(stale, onlyRow);
        db.Set<TenantTimeZoneProjection>().Add(TenantTimeZoneProjection.Create(both, NewYork, 3, DateTime.UtcNow));
        await db.SaveChangesAsync();
        var repository = new OfficeScheduleRepository(db);

        Assert.Equal(NewYork, await repository.GetTimeZoneIdAsync(both));
        Assert.Equal(LaPaz, await repository.GetTimeZoneIdAsync(rowOnly));
        Assert.Null(await repository.GetTimeZoneIdAsync(Guid.NewGuid()));
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
