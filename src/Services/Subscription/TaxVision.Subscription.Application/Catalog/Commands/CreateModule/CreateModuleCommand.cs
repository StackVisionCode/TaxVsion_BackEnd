namespace TaxVision.Subscription.Application.Catalog.Commands.CreateModule;

public sealed record CreateModuleCommand(
    string Code,
    string Name,
    string Description,
    string Category,
    decimal? AddonPriceMonthlyUsd,
    decimal? AddonPriceYearlyUsd,
    Guid ActorUserId
);
