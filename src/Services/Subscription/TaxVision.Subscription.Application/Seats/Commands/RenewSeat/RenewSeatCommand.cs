namespace TaxVision.Subscription.Application.Seats.Commands.RenewSeat;

/// <summary>Renovación manual de un seat disparada por un admin, mientras no exista
/// integración con Billing.</summary>
public sealed record RenewSeatCommand(Guid TenantId, Guid SeatId, Guid RequestedByUserId);
