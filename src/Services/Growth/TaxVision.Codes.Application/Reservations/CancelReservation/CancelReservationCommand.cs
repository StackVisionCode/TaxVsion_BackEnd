namespace TaxVision.Codes.Application.Reservations.CancelReservation;

public sealed record CancelReservationCommand(
    Guid TenantId,
    Guid ReservationId,
    string PaymentSource,
    Guid PaymentId,
    string Reason,
    string IdempotencyKey
);
