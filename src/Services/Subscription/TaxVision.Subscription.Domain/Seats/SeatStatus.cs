namespace TaxVision.Subscription.Domain.Seats;

public enum SeatStatus
{
    Available,
    Assigned,
    Active,
    PastDue,
    GracePeriod,
    Suspended,
    Cancelled,
    Expired,
    Released,
}
