using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Booking;

/// <summary>Errores de la reserva del cliente en el portal.</summary>
public static class BookingErrors
{
    public static readonly Error NotAvailable = new(
        "Calendar.BookingNotAvailable",
        "Online booking isn't available for this appointment."
    );

    public static readonly Error SlotTaken = new("Calendar.SlotTaken", "That time was just taken. Pick another one.");

    public static readonly Error ChangeTooLate = new(
        "Calendar.ChangeTooLate",
        "It's too close to the appointment to change it online. Contact the office."
    );

    public static readonly Error RangeTooLong = new("Calendar.RangeTooLong", "The requested range is too long.");

    public static readonly Error IdempotencyKeyRequired = new(
        "Calendar.IdempotencyKeyRequired",
        "The Idempotency-Key header is required."
    );
}
