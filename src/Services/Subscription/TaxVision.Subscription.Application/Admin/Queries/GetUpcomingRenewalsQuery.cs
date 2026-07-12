namespace TaxVision.Subscription.Application.Admin.Queries;

/// <summary>Cross-tenant — solo PlatformAdmin.</summary>
public sealed record GetUpcomingRenewalsQuery(int DaysAhead);
