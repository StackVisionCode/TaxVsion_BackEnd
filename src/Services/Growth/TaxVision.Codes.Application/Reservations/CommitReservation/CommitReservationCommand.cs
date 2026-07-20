namespace TaxVision.Codes.Application.Reservations.CommitReservation;

public sealed record CommitReservationCommand(
    Guid TenantId,
    Guid ReservationId,
    string PaymentSource,
    Guid PaymentId,
    string SnapshotHash,
    Guid SourceEventId,
    string IdempotencyKey
);
