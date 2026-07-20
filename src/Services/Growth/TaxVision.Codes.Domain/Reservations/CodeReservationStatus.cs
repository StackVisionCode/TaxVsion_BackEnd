namespace TaxVision.Codes.Domain.Reservations;

public enum CodeReservationStatus
{
    Active = 1,
    Committed = 2,
    Cancelled = 3,
    Expired = 4,
    Compensated = 5,
}
