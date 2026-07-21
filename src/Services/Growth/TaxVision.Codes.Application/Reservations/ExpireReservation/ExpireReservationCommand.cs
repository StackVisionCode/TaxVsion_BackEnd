namespace TaxVision.Codes.Application.Reservations.ExpireReservation;

public sealed record ExpireReservationCommand(Guid TenantId, Guid ReservationId, string IdempotencyKey);
