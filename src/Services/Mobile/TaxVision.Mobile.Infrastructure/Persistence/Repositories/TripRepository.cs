using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Application.Trips;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Infrastructure.Persistence.Repositories;

public sealed class TripRepository(MobileDbContext db) : ITripRepository
{
    private IQueryable<Trip> Owned(Guid tenant, Guid customer) =>
        db
            .Trips.IgnoreQueryFilters()
            .Where(x =>
                tenant != Guid.Empty && customer != Guid.Empty && x.TenantId == tenant && x.CustomerId == customer
            );

    public Task<Trip?> FindAsync(Guid tenant, Guid customer, Guid id, CancellationToken ct) =>
        Owned(tenant, customer).SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<Trip?> FindClientAsync(Guid tenant, Guid customer, Guid id, CancellationToken ct) =>
        Owned(tenant, customer).SingleOrDefaultAsync(x => x.ClientTripId == id, ct);

    public Task<TripOperation?> OperationAsync(
        Guid tenant,
        Guid customer,
        Guid trip,
        long sequence,
        CancellationToken ct
    ) =>
        db
            .TripOperations.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenant && x.CustomerId == customer && x.TripId == trip && x.Sequence == sequence,
                ct
            );

    public void Add(Trip trip) => db.Trips.Add(trip);

    public void AddPoint(TripPoint point) => db.TripPoints.Add(point);

    // Explicit ownership predicates protect tenant data even when called without a scoped tenant context.
    public async Task DeletePointsAsync(Guid tenant, Guid customer, Guid trip, CancellationToken ct)
    {
        var points = await db.TripPoints.IgnoreQueryFilters()
            .Where(x => tenant != Guid.Empty && customer != Guid.Empty && trip != Guid.Empty &&
                x.TenantId == tenant && x.CustomerId == customer && x.TripId == trip)
            .ToListAsync(ct);
        db.TripPoints.RemoveRange(points);
    }

    public void AddOperation(TripOperation operation) => db.TripOperations.Add(operation);

    public Task<bool> HasVehicleTripsAsync(Guid tenant, Guid customer, Guid vehicle, CancellationToken ct) =>
        Owned(tenant, customer).AnyAsync(x => x.VehicleId == vehicle, ct);

    public async Task<PagedResult<Trip>> ListAsync(Guid tenant, Guid customer, TripFilter f, CancellationToken ct)
    {
        var query = Owned(tenant, customer).AsNoTracking().Where(x => x.DeletedAtUtc == null);
        if (f.StartDate.HasValue)
        {
            var start = f.StartDate.Value.UtcDateTime;
            query = query.Where(x => x.StartedAtUtc >= start);
        }
        if (f.EndDate.HasValue)
        {
            var end = f.EndDate.Value.UtcDateTime;
            query = query.Where(x => x.StartedAtUtc <= end);
        }
        if (f.Purpose is not null)
            query = query.Where(x => x.Purpose == f.Purpose);
        if (f.Status.HasValue)
            query = query.Where(x => x.Status == f.Status.Value);
        if (f.VehicleId.HasValue)
            query = query.Where(x => x.VehicleId == f.VehicleId.Value);
        if (f.MinDistanceMiles.HasValue)
        {
            var meters = f.MinDistanceMiles.Value * 1609.344;
            query = query.Where(x => x.DistanceMeters >= meters);
        }
        if (f.MaxDistanceMiles.HasValue)
        {
            var meters = f.MaxDistanceMiles.Value * 1609.344;
            query = query.Where(x => x.DistanceMeters <= meters);
        }
        var count = await query.CountAsync(ct);
        var asc = f.SortOrder == "asc";
        IOrderedQueryable<Trip> ordered = f.SortBy switch
        {
            "distance" => asc ? query.OrderBy(x => x.DistanceMeters) : query.OrderByDescending(x => x.DistanceMeters),
            "duration" => asc
                ? query.OrderBy(x => x.ActiveDurationSeconds)
                : query.OrderByDescending(x => x.ActiveDurationSeconds),
            _ => asc ? query.OrderBy(x => x.StartedAtUtc) : query.OrderByDescending(x => x.StartedAtUtc),
        };
        var items = await ordered.ThenBy(x => x.Id).Skip((f.Page - 1) * f.Size).Take(f.Size).ToListAsync(ct);
        return new(items, f.Page, f.Size, count);
    }

    public async Task<IReadOnlyList<Trip>> ActiveAsync(Guid tenant, Guid customer, CancellationToken ct) =>
        await Owned(tenant, customer)
            .AsNoTracking()
            .Where(x => x.DeletedAtUtc == null && (x.Status == TripStatus.InProgress || x.Status == TripStatus.Paused))
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenBy(x => x.Id)
            .Take(100)
            .ToListAsync(ct);

    public async Task<TripPointsPage> PointsAsync(
        Guid tenant,
        Guid customer,
        Guid trip,
        long after,
        int size,
        CancellationToken ct
    )
    {
        var rows = await db
            .TripPoints.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenant && x.CustomerId == customer && x.TripId == trip && x.Sequence > after)
            .OrderBy(x => x.Sequence)
            .Take(size + 1)
            .ToListAsync(ct);
        var items = rows.Take(size).Select(TripPointResponse.From).ToArray();
        return new(items, items.Length > 0 ? items[^1].Sequence : after, rows.Count > size);
    }

    public Task<MileageRate?> EffectiveRateAsync(Guid tenant, string purpose, DateOnly date, CancellationToken ct) =>
        db
            .MileageRates.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Purpose == purpose && x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    public void AddRate(MileageRate rate) => db.MileageRates.Add(rate);

    public async Task<PagedResult<MileageRateResponse>> RatesAsync(
        Guid tenant,
        int page,
        int size,
        CancellationToken ct
    )
    {
        var query = db.MileageRates.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant);
        var count = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
        return new(rows.Select(MileageRateResponse.From).ToArray(), page, size, count);
    }

    private IQueryable<Trip> Completed(Guid tenant, Guid customer) =>
        Owned(tenant, customer).AsNoTracking().Where(x => x.Status == TripStatus.Completed && x.DeletedAtUtc == null);

    private static DateTime Boundary(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        // Algunas zonas adelantan el reloj a medianoche: usar el primer instante civil valido.
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static async Task<StatisticsTotals> Totals(IQueryable<Trip> query, CancellationToken ct) =>
        await query
            .GroupBy(_ => 1)
            .Select(g => new StatisticsTotals(
                g.Count(),
                g.Sum(x => x.DistanceMeters) / 1609.344,
                g.Sum(x => x.ActiveDurationSeconds)
            ))
            .SingleOrDefaultAsync(ct)
        ?? new(0, 0, 0);

    public async Task<TripStatisticsResponse> StatisticsAsync(
        Guid tenant,
        Guid customer,
        DateTime nowUtc,
        TimeZoneInfo zone,
        string iana,
        CancellationToken ct
    )
    {
        var query = Completed(tenant, customer);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone));
        var week = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weekUtc = Boundary(week, zone);
        var monthUtc = Boundary(new(today.Year, today.Month, 1), zone);
        var yearUtc = Boundary(new(today.Year, 1, 1), zone);
        var all = await Totals(query, ct);
        var w = await Totals(query.Where(x => x.StartedAtUtc >= weekUtc && x.StartedAtUtc <= nowUtc), ct);
        var m = await Totals(query.Where(x => x.StartedAtUtc >= monthUtc && x.StartedAtUtc <= nowUtc), ct);
        var y = await Totals(query.Where(x => x.StartedAtUtc >= yearUtc && x.StartedAtUtc <= nowUtc), ct);
        var purposes = await query
            .GroupBy(x => x.Purpose)
            .Select(g => new PurposeMileage(g.Key, g.Sum(x => x.DistanceMeters) / 1609.344))
            .ToListAsync(ct);
        var amounts = await query
            .Where(x => x.Currency != null && x.EstimatedAmount.HasValue)
            .GroupBy(x => x.Currency)
            .Select(g => new CurrencyEstimate(g.Key!, g.Sum(x => x.EstimatedAmount!.Value)))
            .ToListAsync(ct);
        var missing = await query.CountAsync(x => !x.RatePerMile.HasValue, ct);
        return new(
            all.Trips,
            all.Miles,
            all.Miles * 1.609344,
            all.Seconds,
            w.Trips,
            w.Miles,
            m.Trips,
            m.Miles,
            y.Trips,
            y.Miles,
            all.Trips > 0 ? all.Miles / all.Trips : 0,
            all.Trips > 0 ? all.Seconds / all.Trips : 0,
            purposes,
            amounts,
            missing,
            iana
        );
    }

    public async Task<IReadOnlyList<DailyMileageResponse>> DailyAsync(
        Guid tenant,
        Guid customer,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo zone,
        CancellationToken ct
    )
    {
        var start = Boundary(from, zone);
        var end = Boundary(to.AddDays(1), zone);
        // SQL Server usa identificadores Windows para AT TIME ZONE, incluso en Linux.
        var windows = TimeZoneInfo.TryConvertIanaIdToWindowsId(zone.Id, out var mapped) ? mapped : zone.Id;
        var rows = await Completed(tenant, customer)
            .Where(x => x.StartedAtUtc >= start && x.StartedAtUtc < end)
            .GroupBy(x => EF.Functions.AtTimeZone(EF.Functions.AtTimeZone(x.StartedAtUtc, "UTC"), windows).Date)
            .Select(g => new
            {
                Day = g.Key,
                Miles = g.Sum(x => x.DistanceMeters) / 1609.344,
                Count = g.Count(),
            })
            .ToListAsync(ct);
        var byDate = rows.ToDictionary(x => DateOnly.FromDateTime(x.Day));
        var result = new List<DailyMileageResponse>();
        for (var date = from; date <= to; date = date.AddDays(1))
            result.Add(byDate.TryGetValue(date, out var row) ? new(date, row.Miles, row.Count) : new(date, 0, 0));
        return result;
    }
}
