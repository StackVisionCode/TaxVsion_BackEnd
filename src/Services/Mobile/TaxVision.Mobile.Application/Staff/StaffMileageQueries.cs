using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Staff;

// Lecturas del personal sobre el kilometraje de un cliente. Todas empiezan comprobando la visibilidad:
// un cliente no visible responde igual que un viaje inexistente (404), sin revelar que existe.

public sealed record StaffListTripsQuery(
    StaffCustomerScope Scope,
    int? TaxYear,
    string? Purpose,
    TripStatus? Status,
    Guid? VehicleId,
    int Page,
    int Size
);

public sealed record StaffGetTripQuery(StaffCustomerScope Scope, Guid TripId);

public sealed record StaffTripPointsQuery(StaffCustomerScope Scope, Guid TripId, long After, int Size);

public sealed record StaffListVehiclesQuery(StaffCustomerScope Scope);

public sealed record StaffMileageSummaryQuery(StaffCustomerScope Scope, int? TaxYear, Guid? VehicleId);

public sealed record StaffExportTripsQuery(StaffCustomerScope Scope, int? TaxYear);

/// <summary>Año fiscal de las lecturas del personal. Sin año, el actual en la zona de la oficina.</summary>
public static class StaffYear
{
    /// <summary>Tope de página: un número enorme desbordaría el OFFSET y daría un 500.</summary>
    public const int MaxPage = 10_000;

    public static bool IsValid(int? taxYear) => taxYear is null or (>= 2000 and <= 2100);

    /// <summary>El año que corre en la oficina (el 31-dic de noche en EE. UU. ya es 1-ene en UTC).</summary>
    public static int Current(TimeZoneInfo zone, DateTime utcNow) => MileageRateResolver.LocalDate(utcNow, zone).Year;

    /// <summary>Del 1 de enero al 1 de enero siguiente, en la hora local de la oficina, pasado a UTC.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) Range(int taxYear, TimeZoneInfo zone) =>
        (
            TimeZoneInfo.ConvertTimeToUtc(new DateTime(taxYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), zone),
            TimeZoneInfo.ConvertTimeToUtc(new DateTime(taxYear + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), zone)
        );
}

public static class StaffListTripsHandler
{
    public static async Task<Result<PagedResult<StaffTripResponse>>> Handle(
        StaffListTripsQuery q,
        IStaffMileageReader reader,
        IStaffCustomerAssignments assignments,
        CancellationToken ct
    )
    {
        if (!StaffYear.IsValid(q.TaxYear) || q.Page is < 1 or > StaffYear.MaxPage || q.Size is < 1 or > 100)
            return Result.Failure<PagedResult<StaffTripResponse>>(TripErrors.Invalid);
        if (q.Purpose is not null && !TripPurpose.IsValid(q.Purpose))
            return Result.Failure<PagedResult<StaffTripResponse>>(TripErrors.Invalid);
        if (!await StaffCustomerAccess.CanSeeAsync(q.Scope, assignments, ct))
            return Result.Failure<PagedResult<StaffTripResponse>>(TripErrors.NotFound);
        var zone = await reader.OfficeZoneAsync(q.Scope.TenantId, ct);
        var taxYear = q.TaxYear ?? StaffYear.Current(zone, DateTime.UtcNow);
        var (from, to) = StaffYear.Range(taxYear, zone);
        var page = await reader.ListTripsAsync(
            new(q.Scope.TenantId, q.Scope.CustomerId, from, to, q.Purpose, q.Status, q.VehicleId, q.Page, q.Size),
            ct
        );
        var office = await reader.OfficeRatesAsync(q.Scope.TenantId, ct);
        var standard = await reader.StandardRatesAsync(ct);
        return Result.Success(
            new PagedResult<StaffTripResponse>(
                page.Items.Select(row => StaffTripResponse.From(row, office, standard, zone)).ToArray(),
                page.Page,
                page.Size,
                page.TotalCount
            )
        );
    }
}

public static class StaffGetTripHandler
{
    public static async Task<Result<StaffTripResponse>> Handle(
        StaffGetTripQuery q,
        IStaffMileageReader reader,
        IStaffCustomerAssignments assignments,
        CancellationToken ct
    )
    {
        if (!await StaffCustomerAccess.CanSeeAsync(q.Scope, assignments, ct))
            return Result.Failure<StaffTripResponse>(TripErrors.NotFound);
        var row = await reader.FindTripAsync(q.Scope.TenantId, q.Scope.CustomerId, q.TripId, ct);
        if (row is null)
            return Result.Failure<StaffTripResponse>(TripErrors.NotFound);
        var zone = await reader.OfficeZoneAsync(q.Scope.TenantId, ct);
        return Result.Success(
            StaffTripResponse.From(
                row,
                await reader.OfficeRatesAsync(q.Scope.TenantId, ct),
                await reader.StandardRatesAsync(ct),
                zone
            )
        );
    }
}

public static class StaffTripPointsHandler
{
    public static async Task<Result<TripPointsPage>> Handle(
        StaffTripPointsQuery q,
        IStaffMileageReader reader,
        IStaffCustomerAssignments assignments,
        ITripRepository trips,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (q.After < -1 || q.Size is < 1 or > 512)
            return Result.Failure<TripPointsPage>(TripErrors.Invalid);
        if (!await StaffCustomerAccess.CanSeeAsync(q.Scope, assignments, ct))
            return Result.Failure<TripPointsPage>(TripErrors.NotFound);
        if (await reader.FindTripAsync(q.Scope.TenantId, q.Scope.CustomerId, q.TripId, ct) is null)
            return Result.Failure<TripPointsPage>(TripErrors.NotFound);
        var page = await trips.PointsAsync(q.Scope.TenantId, q.Scope.CustomerId, q.TripId, q.After, q.Size, ct);
        // Cada página leída queda registrada: pedir desde otro punto no evita el rastro.
        reader.Log(
            StaffMileageAccessLog.Record(
                q.Scope.TenantId,
                q.Scope.StaffUserId,
                q.Scope.CustomerId,
                q.TripId,
                StaffMileageAccessLog.RouteViewed,
                clock.GetUtcNow().UtcDateTime
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(page);
    }
}

public static class StaffListVehiclesHandler
{
    public static async Task<Result<IReadOnlyList<VehicleResponse>>> Handle(
        StaffListVehiclesQuery q,
        IStaffMileageReader reader,
        IStaffCustomerAssignments assignments,
        CancellationToken ct
    )
    {
        if (!await StaffCustomerAccess.CanSeeAsync(q.Scope, assignments, ct))
            return Result.Failure<IReadOnlyList<VehicleResponse>>(TripErrors.NotFound);
        return Result.Success(await reader.VehiclesAsync(q.Scope.TenantId, q.Scope.CustomerId, ct));
    }
}

public static class StaffMileageSummaryHandler
{
    public static async Task<Result<StaffMileageSummaryResponse>> Handle(
        StaffMileageSummaryQuery q,
        IStaffMileageReader reader,
        IStaffCustomerAssignments assignments,
        CancellationToken ct
    )
    {
        if (!StaffYear.IsValid(q.TaxYear))
            return Result.Failure<StaffMileageSummaryResponse>(TripErrors.Invalid);
        if (!await StaffCustomerAccess.CanSeeAsync(q.Scope, assignments, ct))
            return Result.Failure<StaffMileageSummaryResponse>(TripErrors.NotFound);
        var zone = await reader.OfficeZoneAsync(q.Scope.TenantId, ct);
        var taxYear = q.TaxYear ?? StaffYear.Current(zone, DateTime.UtcNow);
        var (from, to) = StaffYear.Range(taxYear, zone);
        var rows = await reader.TripsInRangeAsync(q.Scope.TenantId, q.Scope.CustomerId, from, to, q.VehicleId, ct);
        return Result.Success(
            StaffMileageSummary.Build(
                taxYear,
                rows,
                await reader.OfficeRatesAsync(q.Scope.TenantId, ct),
                await reader.StandardRatesAsync(ct),
                zone
            )
        );
    }
}

public static class StaffExportTripsHandler
{
    public static async Task<Result<StaffTripsExport>> Handle(
        StaffExportTripsQuery q,
        IStaffMileageReader reader,
        IStaffCustomerAssignments assignments,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (!StaffYear.IsValid(q.TaxYear))
            return Result.Failure<StaffTripsExport>(TripErrors.Invalid);
        if (!await StaffCustomerAccess.CanSeeAsync(q.Scope, assignments, ct))
            return Result.Failure<StaffTripsExport>(TripErrors.NotFound);
        var zone = await reader.OfficeZoneAsync(q.Scope.TenantId, ct);
        var taxYear = q.TaxYear ?? StaffYear.Current(zone, clock.GetUtcNow().UtcDateTime);
        var (from, to) = StaffYear.Range(taxYear, zone);
        var rows = await reader.TripsInRangeAsync(q.Scope.TenantId, q.Scope.CustomerId, from, to, null, ct);
        var content = StaffTripsCsv.Write(
            rows,
            await reader.OfficeRatesAsync(q.Scope.TenantId, ct),
            await reader.StandardRatesAsync(ct),
            zone
        );
        reader.Log(
            StaffMileageAccessLog.Record(
                q.Scope.TenantId,
                q.Scope.StaffUserId,
                q.Scope.CustomerId,
                null,
                StaffMileageAccessLog.TripsExported,
                clock.GetUtcNow().UtcDateTime
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        // El nombre del archivo no lleva el nombre del cliente.
        return Result.Success(new StaffTripsExport($"mileage-log-{taxYear}.csv", content));
    }
}
