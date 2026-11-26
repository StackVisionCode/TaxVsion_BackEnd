using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TenantTimeZones;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Application.Staff;
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Infrastructure.Persistence;

namespace TaxVision.Mobile.Infrastructure.Staff;

/// <summary>
/// Lecturas del personal. Sin filtro global: oficina y cliente van explícitos en cada consulta. Proyecta solo
/// las columnas necesarias; los puntos GPS solo se cuentan.
/// </summary>
internal sealed class StaffMileageReader(MobileDbContext db, ITenantTimeZoneResolver zones) : IStaffMileageReader
{
    public async Task<TimeZoneInfo> OfficeZoneAsync(Guid tenantId, CancellationToken ct) =>
        (await zones.ResolveAsync(tenantId, ct)).Zone;

    public async Task<PagedResult<StaffTripRow>> ListTripsAsync(StaffTripListRequest r, CancellationToken ct)
    {
        var query = Trips(r.TenantId, r.CustomerId, r.FromUtc, r.ToUtc);
        if (r.Purpose is not null)
            query = query.Where(x => x.Purpose == r.Purpose);
        if (r.Status.HasValue)
            query = query.Where(x => x.Status == r.Status.Value);
        if (r.VehicleId.HasValue)
            query = query.Where(x => x.VehicleId == r.VehicleId.Value);
        var total = await query.CountAsync(ct);
        var items = await Project(
                query.OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id).Skip((r.Page - 1) * r.Size).Take(r.Size),
                withPointCount: true
            )
            .ToListAsync(ct);
        return new(items, r.Page, r.Size, total);
    }

    public async Task<IReadOnlyList<StaffTripRow>> TripsInRangeAsync(
        Guid tenantId,
        Guid customerId,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? vehicleId,
        CancellationToken ct
    )
    {
        var query = Trips(tenantId, customerId, fromUtc, toUtc);
        if (vehicleId.HasValue)
            query = query.Where(x => x.VehicleId == vehicleId.Value);
        return await Project(query, withPointCount: false).ToListAsync(ct);
    }

    public Task<StaffTripRow?> FindTripAsync(Guid tenantId, Guid customerId, Guid tripId, CancellationToken ct) =>
        Project(
                db.Trips.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x =>
                        x.TenantId == tenantId && x.CustomerId == customerId && x.Id == tripId && x.DeletedAtUtc == null
                    ),
                withPointCount: true
            )
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<RatePeriod>> OfficeRatesAsync(Guid tenantId, CancellationToken ct) =>
        await db
            .MileageRates.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new RatePeriod(x.Purpose, x.EffectiveFrom, x.RatePerMile, x.Currency))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RatePeriod>> StandardRatesAsync(CancellationToken ct) =>
        await db
            .StandardMileageRates.AsNoTracking()
            .Select(x => new RatePeriod(x.Purpose, x.EffectiveFrom, x.RatePerMile, x.Currency))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<VehicleResponse>> VehiclesAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct
    )
    {
        var vehicles = await db
            .Vehicles.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CustomerId == customerId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);
        return vehicles.Select(VehicleResponse.From).ToArray();
    }

    public void Log(StaffMileageAccessLog entry) => db.StaffMileageAccessLogs.Add(entry);

    private IQueryable<Trip> Trips(Guid tenantId, Guid customerId, DateTime fromUtc, DateTime toUtc) =>
        db
            .Trips.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId
                && x.CustomerId == customerId
                && x.DeletedAtUtc == null
                && x.StartedAtUtc >= fromUtc
                && x.StartedAtUtc < toUtc
            );

    private IQueryable<StaffTripRow> Project(IQueryable<Trip> trips, bool withPointCount) =>
        trips.Select(x => new StaffTripRow(
            x.Id,
            x.VehicleId,
            x.VehiclePlate,
            x.Status,
            x.Purpose,
            x.Notes,
            x.StartedAtUtc,
            x.EndedAtUtc,
            x.DistanceMeters,
            x.ActiveDurationSeconds,
            x.MaxSpeedMetersPerSecond,
            x.RatePerMile,
            x.Currency,
            x.CoordinatesPurgedAtUtc,
            withPointCount
                ? db
                    .TripPoints.IgnoreQueryFilters()
                    .Count(p => p.TenantId == x.TenantId && p.CustomerId == x.CustomerId && p.TripId == x.Id)
                : 0
        ));
}

/// <summary>Asignación cliente → personal leída de la proyección compartida.</summary>
internal sealed class StaffCustomerAssignments(ICustomerAssignmentProjectionStore store) : IStaffCustomerAssignments
{
    public Task<bool> IsAssignedAsync(Guid tenantId, Guid userId, Guid customerId, CancellationToken ct) =>
        store.IsAnyAssignedToUserAsync(tenantId, userId, [customerId], ct);
}

/// <summary>Token M2M para reconciliar asignaciones: el listado cruza oficinas, se pide como plataforma.</summary>
internal sealed class MobilePlatformTokenProvider(IServiceTokenAcquirer tokens) : IPlatformServiceTokenProvider
{
    public Task<string?> GetPlatformTokenAsync(CancellationToken ct = default) =>
        tokens.GetTokenAsync(PlatformTenant.Id, ct);
}
