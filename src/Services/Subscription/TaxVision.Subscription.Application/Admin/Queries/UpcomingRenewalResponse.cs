namespace TaxVision.Subscription.Application.Admin.Queries;

public sealed record UpcomingRenewalResponse(Guid TenantId, string AggregateType, Guid AggregateId, DateTime DueAtUtc);
