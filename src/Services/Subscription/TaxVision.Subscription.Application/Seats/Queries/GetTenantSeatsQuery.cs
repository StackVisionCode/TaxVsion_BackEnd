namespace TaxVision.Subscription.Application.Seats.Queries;

public sealed record GetTenantSeatsQuery(Guid TenantId, string? Status, string? Type, int Page, int PageSize);
