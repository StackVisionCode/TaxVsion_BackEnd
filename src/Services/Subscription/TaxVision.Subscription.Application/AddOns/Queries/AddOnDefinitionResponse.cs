namespace TaxVision.Subscription.Application.AddOns.Queries;

public sealed record AddOnDefinitionResponse(
    Guid Id,
    string Code,
    string Name,
    string Description,
    string Category,
    bool AllowMultipleInstances
);
