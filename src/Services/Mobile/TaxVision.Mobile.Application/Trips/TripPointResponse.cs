using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record TripPointResponse(
    long Sequence,
    DateTime ObservedAt,
    double Latitude,
    double Longitude,
    double AccuracyMeters,
    double? SpeedMetersPerSecond,
    double? AltitudeMeters,
    double? Heading
)
{
    public static TripPointResponse From(TripPoint p) =>
        new(
            p.Sequence,
            p.ObservedAtUtc,
            p.Latitude,
            p.Longitude,
            p.AccuracyMeters,
            p.SpeedMetersPerSecond,
            p.AltitudeMeters,
            p.Heading
        );
}
