namespace TaxVision.Subscription.Application.Seats.Queries;

public sealed record GetSeatByIdQuery(Guid TenantId, Guid SeatId);
