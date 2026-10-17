namespace TaxVision.Subscription.Application.Catalog.Commands.UpdateModule;

public sealed record UpdateModuleCommand(
    string Code,
    string Category,
    decimal? AddonPriceMonthlyUsd,
    decimal? AddonPriceYearlyUsd,
    Guid ActorUserId
);
