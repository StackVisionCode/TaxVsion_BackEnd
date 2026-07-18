namespace TaxVision.Subscription.Application.Entitlements.Queries;

public sealed record GetTenantEntitlementByKeyQuery(Guid TenantId, string Key);
