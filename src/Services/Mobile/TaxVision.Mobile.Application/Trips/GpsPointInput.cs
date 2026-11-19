using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips;

public sealed record GpsPointInput(
    long Sequence,
    DateTimeOffset ObservedAt,
    double Latitude,
    double Longitude,
    double AccuracyMeters,
    double? SpeedMetersPerSecond = null,
    double? AltitudeMeters = null,
    double? Heading = null
)
{
    public GpsSample ToSample() =>
        new(
            Sequence,
            ObservedAt.UtcDateTime,
            Latitude,
            Longitude,
            AccuracyMeters,
            SpeedMetersPerSecond,
            AltitudeMeters,
            Heading
        );
}
