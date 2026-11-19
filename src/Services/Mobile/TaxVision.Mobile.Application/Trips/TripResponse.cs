using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record TripResponse(
    Guid Id,
    Guid ClientTripId,
    Guid VehicleId,
    string VehiclePlate,
    TripStatus Status,
    string Purpose,
    string? Notes,
    DateTime StartTime,
    DateTime? EndTime,
    double DistanceMeters,
    double DistanceMiles,
    double DistanceKm,
    double DurationSeconds,
    double AverageSpeedMph,
    double MaxSpeedMph,
    long Revision,
    long LastAcceptedPointSequence,
    Guid? MileageRateId,
    decimal? RatePerMile,
    string? Currency,
    decimal? EstimatedAmount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Replayed = false
)
{
    public static TripResponse From(Trip t, bool replayed = false) =>
        new(
            t.Id,
            t.ClientTripId,
            t.VehicleId,
            t.VehiclePlate,
            t.Status,
            t.Purpose,
            t.Notes,
            t.StartedAtUtc,
            t.EndedAtUtc,
            t.DistanceMeters,
            t.DistanceMeters / 1609.344,
            t.DistanceMeters / 1000,
            t.ActiveDurationSeconds,
            t.ActiveDurationSeconds > 0 ? t.DistanceMeters / t.ActiveDurationSeconds * 2.236936292 : 0,
            t.MaxSpeedMetersPerSecond * 2.236936292,
            t.Revision,
            t.LastPointSequence,
            t.MileageRateId,
            t.RatePerMile,
            t.Currency,
            t.EstimatedAmount,
            t.CreatedAtUtc,
            t.UpdatedAtUtc,
            replayed
        );
}
