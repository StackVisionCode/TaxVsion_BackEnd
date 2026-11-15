namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>La reserva recién pedida y sus secretos en claro, que salen una sola vez.</summary>
public sealed record PublicBookingIssued(PublicBooking Booking, string RequestHandle, string? Code);
