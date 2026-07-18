namespace TaxVision.Subscription.Application.Entitlements.Queries;

public sealed record EntitlementValueResponse(
    string ValueType,
    string Value,
    string Status,
    string Source,
    DateTime? ExpiresAtUtc
);
