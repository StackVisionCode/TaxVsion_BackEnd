using BuildingBlocks.Common;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Abstractions;

public interface ITripRepository
{
    Task<Trip?> FindAsync(Guid tenant, Guid customer, Guid id, CancellationToken ct);
    Task<Trip?> FindClientAsync(Guid tenant, Guid customer, Guid clientId, CancellationToken ct);
    Task<TripOperation?> OperationAsync(Guid tenant, Guid customer, Guid tripId, long sequence, CancellationToken ct);
    void Add(Trip trip);
    void AddPoint(TripPoint point);
    Task DeletePointsAsync(Guid tenant, Guid customer, Guid trip, CancellationToken ct);
    void AddOperation(TripOperation operation);
    Task<bool> HasVehicleTripsAsync(Guid tenant, Guid customer, Guid vehicle, CancellationToken ct);
    Task<PagedResult<Trip>> ListAsync(Guid tenant, Guid customer, TripFilter filter, CancellationToken ct);
    Task<IReadOnlyList<Trip>> ActiveAsync(Guid tenant, Guid customer, CancellationToken ct);
    Task<TripPointsPage> PointsAsync(
        Guid tenant,
        Guid customer,
        Guid tripId,
        long after,
        int size,
        CancellationToken ct
    );
    Task<MileageRate?> EffectiveRateAsync(Guid tenant, string purpose, DateOnly date, CancellationToken ct);
    void AddRate(MileageRate rate);
    Task<PagedResult<MileageRateResponse>> RatesAsync(Guid tenant, int page, int size, CancellationToken ct);
    Task<TripStatisticsResponse> StatisticsAsync(
        Guid tenant,
        Guid customer,
        DateTime nowUtc,
        TimeZoneInfo zone,
        string iana,
        CancellationToken ct
    );
    Task<IReadOnlyList<DailyMileageResponse>> DailyAsync(
        Guid tenant,
        Guid customer,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo zone,
        CancellationToken ct
    );
}
