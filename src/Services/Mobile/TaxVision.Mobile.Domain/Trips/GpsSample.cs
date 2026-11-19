namespace TaxVision.Mobile.Domain.Trips;

public sealed record GpsSample(
    long Sequence,
    DateTime ObservedAtUtc,
    double Latitude,
    double Longitude,
    double AccuracyMeters,
    double? SpeedMetersPerSecond = null,
    double? AltitudeMeters = null,
    double? Heading = null
)
{
    public bool IsValid() =>
        Sequence >= 0
        && ObservedAtUtc.Kind == DateTimeKind.Utc
        && double.IsFinite(Latitude)
        && Latitude is >= -90 and <= 90
        && double.IsFinite(Longitude)
        && Longitude is >= -180 and <= 180
        && double.IsFinite(AccuracyMeters)
        && AccuracyMeters is >= 0 and <= 10000
        && (
            SpeedMetersPerSecond is null
            || double.IsFinite(SpeedMetersPerSecond.Value) && SpeedMetersPerSecond is >= 0 and <= 500
        )
        && (
            AltitudeMeters is null || double.IsFinite(AltitudeMeters.Value) && AltitudeMeters is >= -15000 and <= 100000
        )
        && (Heading is null || double.IsFinite(Heading.Value) && Heading is >= 0 and < 360);
}
