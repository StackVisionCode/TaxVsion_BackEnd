namespace TaxVision.Subscription.Application.Plans.Queries;

public sealed record PlanResponse(
    Guid Id,
    string Code,
    string Name,
    string Description,
    string Tier,
    decimal MonthlyPriceUsd,
    int MaxUsers,
    int MaxPendingInvitations,
    long StorageQuotaBytes,
    IReadOnlyList<string> EnabledModules
);
