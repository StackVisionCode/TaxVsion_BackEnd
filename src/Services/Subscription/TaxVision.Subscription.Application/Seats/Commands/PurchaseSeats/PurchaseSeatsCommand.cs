namespace TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;

public sealed record PurchaseSeatsCommand(Guid TenantId, string SeatType, int Quantity, bool AutoRenew, Guid RequestedByUserId);
