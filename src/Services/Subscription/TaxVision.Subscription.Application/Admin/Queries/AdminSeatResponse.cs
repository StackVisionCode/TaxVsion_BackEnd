namespace TaxVision.Subscription.Application.Admin.Queries;

public sealed record AdminSeatResponse(Guid TenantId, Guid SeatId, string Status, DateTime? ExpiredAtUtc);
