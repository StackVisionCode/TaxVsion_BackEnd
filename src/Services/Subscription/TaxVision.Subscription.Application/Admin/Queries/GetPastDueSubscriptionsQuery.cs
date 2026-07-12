namespace TaxVision.Subscription.Application.Admin.Queries;

/// <summary>Cross-tenant — solo PlatformAdmin.</summary>
public sealed record GetPastDueSubscriptionsQuery(int Page, int PageSize);
