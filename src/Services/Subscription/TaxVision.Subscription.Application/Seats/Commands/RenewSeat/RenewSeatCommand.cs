namespace TaxVision.Subscription.Application.Seats.Commands.RenewSeat;

/// <summary>Extensión sin cobro del período de un seat. Solo soporte de plataforma.</summary>
public sealed record RenewSeatCommand(Guid TenantId, Guid SeatId, Guid RequestedByUserId);
