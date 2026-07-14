namespace TaxVision.Subscription.Application.Seats.Queries;

public sealed record GetTenantSeatsQuery(Guid TenantId, string? Status, string? Type, Guid? UserId, int Page, int PageSize);
