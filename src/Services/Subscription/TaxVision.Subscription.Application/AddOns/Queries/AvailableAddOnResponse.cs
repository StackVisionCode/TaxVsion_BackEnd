namespace TaxVision.Subscription.Application.AddOns.Queries;

public sealed record AvailableAddOnResponse(
    string Code,
    string Name,
    string Description,
    string Category,
    decimal? MonthlyUsd,
    decimal? YearlyUsd,
    string? MutexGroup,
    IReadOnlyList<string> Modules
);
