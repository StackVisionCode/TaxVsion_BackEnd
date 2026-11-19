using BuildingBlocks.Domain;

namespace TaxVision.Mobile.Domain.Trips;

public sealed class TripOperation : TenantEntity
{
    private TripOperation() { }

    public Guid CustomerId { get; private set; }
    public Guid TripId { get; private set; }
    public long Sequence { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTime ObservedAtUtc { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }

    public static TripOperation Create(Trip trip, string kind, string hash, DateTime atUtc, DateTime nowUtc)
    {
        var op = new TripOperation
        {
            Id = Guid.NewGuid(),
            TripId = trip.Id,
            CustomerId = trip.CustomerId,
            Sequence = trip.Revision,
            Kind = kind,
            PayloadHash = hash,
            ObservedAtUtc = atUtc,
            ReceivedAtUtc = nowUtc,
        };
        op.SetTenant(trip.TenantId);
        return op;
    }
}
