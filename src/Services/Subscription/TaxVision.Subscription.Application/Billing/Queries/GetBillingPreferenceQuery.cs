namespace TaxVision.Subscription.Application.Billing.Queries;

public sealed record GetBillingPreferenceQuery(Guid TenantId);

public sealed record BillingPreferenceResponse(
    string Mode,
    string? ProviderCode,
    bool HasActiveMandate,
    DateTime? AcceptedAtUtc,
    long? AgreedAmountCents,
    string? AgreedCurrency,
    string? AgreedCadence,
    string? ConsentTextVersion
);
