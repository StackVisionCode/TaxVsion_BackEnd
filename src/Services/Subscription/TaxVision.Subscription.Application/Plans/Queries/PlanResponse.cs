namespace TaxVision.Subscription.Application.Plans.Queries;

public sealed record PlanResponse(
    Guid Id,
    string Code,
    string Name,
    string Description,
    string Tier,
    decimal MonthlyPriceUsd,
    IReadOnlyList<string> SupportedBillingCycles,
    IReadOnlyDictionary<string, decimal> PricesUsdByCycle,
    int MaxUsers,
    int MaxPendingInvitations,
    long StorageQuotaBytes,
    IReadOnlyList<string> EnabledModules
);
