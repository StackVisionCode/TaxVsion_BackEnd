using System.Globalization;
using System.Text;
using BuildingBlocks.Common;
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Staff;

/// <summary>Quién pide y de qué cliente. La oficina y el usuario salen del token.</summary>
public sealed record StaffCustomerScope(Guid TenantId, Guid StaffUserId, bool CanViewAllCustomers, Guid CustomerId);

/// <summary>¿El cliente está asignado a este miembro del personal? La implementa Infrastructure con la proyección.</summary>
public interface IStaffCustomerAssignments
{
    Task<bool> IsAssignedAsync(Guid tenantId, Guid userId, Guid customerId, CancellationToken ct);
}

/// <summary>Lecturas del personal: proyecciones ligeras, sin cargar agregados ni puntos para sumar.</summary>
public interface IStaffMileageReader
{
    Task<TimeZoneInfo> OfficeZoneAsync(Guid tenantId, CancellationToken ct);
    Task<PagedResult<StaffTripRow>> ListTripsAsync(StaffTripListRequest request, CancellationToken ct);
    Task<IReadOnlyList<StaffTripRow>> TripsInRangeAsync(
        Guid tenantId,
        Guid customerId,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? vehicleId,
        CancellationToken ct
    );
    Task<StaffTripRow?> FindTripAsync(Guid tenantId, Guid customerId, Guid tripId, CancellationToken ct);
    Task<IReadOnlyList<RatePeriod>> OfficeRatesAsync(Guid tenantId, CancellationToken ct);
    Task<IReadOnlyList<RatePeriod>> StandardRatesAsync(CancellationToken ct);
    Task<IReadOnlyList<VehicleResponse>> VehiclesAsync(Guid tenantId, Guid customerId, CancellationToken ct);
    void Log(StaffMileageAccessLog entry);
}

public sealed record StaffTripListRequest(
    Guid TenantId,
    Guid CustomerId,
    DateTime FromUtc,
    DateTime ToUtc,
    string? Purpose,
    TripStatus? Status,
    Guid? VehicleId,
    int Page,
    int Size
);

/// <summary>Columnas de un viaje que necesita el personal (nunca sus puntos).</summary>
public sealed record StaffTripRow(
    Guid Id,
    Guid VehicleId,
    string VehiclePlate,
    TripStatus Status,
    string Purpose,
    string? Notes,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    double DistanceMeters,
    double ActiveDurationSeconds,
    double MaxSpeedMetersPerSecond,
    decimal? RatePerMile,
    string? Currency,
    DateTime? CoordinatesPurgedAtUtc,
    int PointCount
);

public sealed record StaffTripResponse(
    Guid Id,
    Guid VehicleId,
    string VehiclePlate,
    TripStatus Status,
    string Purpose,
    string? Notes,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    double DistanceMeters,
    double ActiveDurationSeconds,
    double MaxSpeedMetersPerSecond,
    decimal? RatePerMile,
    string? Currency,
    decimal? EstimatedAmount,
    string? RateSource,
    bool RouteRemoved,
    int PointCount
)
{
    /// <summary>La tasa se resuelve al leer (no se reescribe el viaje); el estimado solo para viajes terminados.</summary>
    public static StaffTripResponse From(
        StaffTripRow row,
        IReadOnlyList<RatePeriod> officeRates,
        IReadOnlyList<RatePeriod> standardRates,
        TimeZoneInfo zone
    )
    {
        var rate = StaffMileageRates.For(row, officeRates, standardRates, zone);
        return new(
            row.Id,
            row.VehicleId,
            row.VehiclePlate,
            row.Status,
            row.Purpose,
            row.Notes,
            row.StartedAtUtc,
            row.EndedAtUtc,
            row.DistanceMeters,
            row.ActiveDurationSeconds,
            row.MaxSpeedMetersPerSecond,
            rate?.RatePerMile,
            rate?.Currency,
            rate is not null && row.Status == TripStatus.Completed ? rate.EstimateFor(row.DistanceMeters) : null,
            rate?.Source.ToString(),
            row.CoordinatesPurgedAtUtc.HasValue,
            row.PointCount
        );
    }
}

/// <summary><c>EstimatedAmount</c> es nulo si los viajes de ese propósito tienen tarifas en monedas distintas.</summary>
public sealed record StaffPurposeTotal(string Purpose, int Trips, double Miles, decimal? EstimatedAmount);

/// <summary>Estimado total en una moneda.</summary>
public sealed record StaffCurrencyAmount(string Currency, decimal Amount);

public sealed record StaffVehicleTotal(Guid VehicleId, string Plate, int Trips, double Miles);

public sealed record StaffMonthTotal(int Month, IReadOnlyDictionary<string, double> MilesByPurpose);

public sealed record StaffMileageSummaryResponse(
    int TaxYear,
    int CompletedTrips,
    double TotalMiles,
    decimal? EstimatedAmount,
    IReadOnlyList<StaffPurposeTotal> ByPurpose,
    IReadOnlyList<StaffVehicleTotal> ByVehicle,
    IReadOnlyList<StaffMonthTotal> ByMonth,
    int InProgressTrips,
    IReadOnlyList<StaffCurrencyAmount> EstimatedAmounts
);

public sealed record StaffTripsExport(string FileName, string Content);

internal static class StaffMileageRates
{
    public static ResolvedRate? For(
        StaffTripRow row,
        IReadOnlyList<RatePeriod> officeRates,
        IReadOnlyList<RatePeriod> standardRates,
        TimeZoneInfo zone
    ) =>
        MileageRateResolver.Resolve(
            row.RatePerMile,
            row.Currency,
            row.Purpose,
            MileageRateResolver.LocalDate(row.StartedAtUtc, zone),
            officeRates,
            standardRates
        );
}

/// <summary>Resumen del año: solo viajes terminados suman; los en curso se cuentan aparte.</summary>
public static class StaffMileageSummary
{
    private const double MetersPerMile = 1609.344;

    public static StaffMileageSummaryResponse Build(
        int taxYear,
        IReadOnlyList<StaffTripRow> rows,
        IReadOnlyList<RatePeriod> officeRates,
        IReadOnlyList<RatePeriod> standardRates,
        TimeZoneInfo zone
    )
    {
        var completed = rows.Where(r => r.Status == TripStatus.Completed).ToArray();
        // Cada estimado con su moneda: montos de monedas distintas nunca se suman.
        var estimates = completed.ToDictionary(
            r => r.Id,
            r =>
                StaffMileageRates.For(r, officeRates, standardRates, zone) is { } rate
                    ? (Amount: rate.EstimateFor(r.DistanceMeters), Currency: (string?)rate.Currency)
                    : (Amount: 0m, Currency: null)
        );
        var byPurpose = completed
            .GroupBy(r => r.Purpose)
            .Select(g => new StaffPurposeTotal(
                g.Key,
                g.Count(),
                Miles(g),
                SingleCurrencyTotal(g.Select(r => estimates[r.Id]))
            ))
            .OrderByDescending(p => p.Miles)
            .ToArray();
        var byVehicle = completed
            .GroupBy(r => (r.VehicleId, r.VehiclePlate))
            .Select(g => new StaffVehicleTotal(g.Key.VehicleId, g.Key.VehiclePlate, g.Count(), Miles(g)))
            .OrderByDescending(v => v.Miles)
            .ToArray();
        var byMonth = Enumerable
            .Range(1, 12)
            .Select(month => new StaffMonthTotal(
                month,
                completed
                    .Where(r => MileageRateResolver.LocalDate(r.StartedAtUtc, zone).Month == month)
                    .GroupBy(r => r.Purpose)
                    .ToDictionary(g => g.Key, Miles)
            ))
            .ToArray();
        return new(
            taxYear,
            completed.Length,
            Miles(completed),
            SingleCurrencyTotal(estimates.Values),
            byPurpose,
            byVehicle,
            byMonth,
            rows.Count(r => r.Status is TripStatus.InProgress or TripStatus.Paused),
            estimates
                .Values.Where(e => e.Currency is not null)
                .GroupBy(e => e.Currency!)
                .Select(g => new StaffCurrencyAmount(g.Key, g.Sum(e => e.Amount)))
                .OrderByDescending(a => a.Amount)
                .ToArray()
        );
    }

    /// <summary>La suma si todo está en una sola moneda (o no hay tarifa); nulo si se mezclan monedas.</summary>
    private static decimal? SingleCurrencyTotal(IEnumerable<(decimal Amount, string? Currency)> estimates)
    {
        var list = estimates.ToList();
        return list.Where(e => e.Currency is not null).Select(e => e.Currency).Distinct().Count() > 1
            ? null
            : list.Sum(e => e.Amount);
    }

    private static double Miles(IEnumerable<StaffTripRow> rows) => rows.Sum(r => r.DistanceMeters) / MetersPerMile;
}

/// <summary>CSV del registro del año (solo terminados). Neutraliza lo que una hoja de cálculo ejecutaría como fórmula.</summary>
public static class StaffTripsCsv
{
    private const double MetersPerMile = 1609.344;

    public static string Write(
        IReadOnlyList<StaffTripRow> rows,
        IReadOnlyList<RatePeriod> officeRates,
        IReadOnlyList<RatePeriod> standardRates,
        TimeZoneInfo zone
    )
    {
        var csv = new StringBuilder();
        csv.Append("Date,Purpose,Notes,Vehicle,Miles,Duration (min),Rate per mile,Estimated amount\r\n");
        foreach (var row in rows.Where(r => r.Status == TripStatus.Completed).OrderBy(r => r.StartedAtUtc))
        {
            var rate = StaffMileageRates.For(row, officeRates, standardRates, zone);
            string[] fields =
            [
                MileageRateResolver
                    .LocalDate(row.StartedAtUtc, zone)
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.Purpose,
                row.Notes ?? "",
                row.VehiclePlate,
                (row.DistanceMeters / MetersPerMile).ToString("0.00", CultureInfo.InvariantCulture),
                (row.ActiveDurationSeconds / 60).ToString("0", CultureInfo.InvariantCulture),
                rate?.RatePerMile.ToString("0.000", CultureInfo.InvariantCulture) ?? "",
                rate?.EstimateFor(row.DistanceMeters).ToString("0.00", CultureInfo.InvariantCulture) ?? "",
            ];
            csv.Append(string.Join(',', fields.Select(Field))).Append("\r\n");
        }
        return csv.ToString();
    }

    private static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

/// <summary>Con customers.view_all, cualquier cliente de la oficina; sin él, solo los asignados.</summary>
public static class StaffCustomerAccess
{
    public static async Task<bool> CanSeeAsync(
        StaffCustomerScope scope,
        IStaffCustomerAssignments assignments,
        CancellationToken ct
    ) =>
        scope.CustomerId != Guid.Empty
        && (
            scope.CanViewAllCustomers
            || await assignments.IsAssignedAsync(scope.TenantId, scope.StaffUserId, scope.CustomerId, ct)
        );
}
