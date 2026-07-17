namespace TaxVision.Subscription.Application.Admin.Queries;

public sealed record AdminSubscriptionResponse(
    Guid TenantId,
    Guid TenantSubscriptionId,
    string PlanCode,
    string Status,
    DateTime? NextRenewalAtUtc
);
