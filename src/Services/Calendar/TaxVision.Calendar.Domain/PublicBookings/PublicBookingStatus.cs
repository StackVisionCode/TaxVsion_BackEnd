namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>Estado de la reserva pública. Los valores se guardan en la base: no reordenar.</summary>
public enum PublicBookingStatus
{
    PendingVerification = 0,
    Confirmed = 1,
    Cancelled = 2,
    Expired = 3,
    Completed = 4,
    NoShow = 5,
}
