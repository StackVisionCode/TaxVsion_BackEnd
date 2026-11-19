using BuildingBlocks.Domain;

namespace TaxVision.Mobile.Domain.Trips;

public sealed class TripPoint : TenantEntity
{
    private TripPoint() { }

    public Guid CustomerId { get; private set; }
    public Guid TripId { get; private set; }
    public long Sequence { get; private set; }
    public DateTime ObservedAtUtc { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public double AccuracyMeters { get; private set; }
    public double? SpeedMetersPerSecond { get; private set; }
    public double? AltitudeMeters { get; private set; }
    public double? Heading { get; private set; }

    public static TripPoint From(Trip trip, GpsSample sample)
    {
        var point = new TripPoint
        {
            Id = Guid.NewGuid(),
            CustomerId = trip.CustomerId,
            TripId = trip.Id,
            Sequence = sample.Sequence,
            ObservedAtUtc = sample.ObservedAtUtc,
            Latitude = sample.Latitude,
            Longitude = sample.Longitude,
            AccuracyMeters = sample.AccuracyMeters,
            SpeedMetersPerSecond = sample.SpeedMetersPerSecond,
            AltitudeMeters = sample.AltitudeMeters,
            Heading = sample.Heading,
        };
        point.SetTenant(trip.TenantId);
        return point;
    }
}
