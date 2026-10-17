namespace TaxVision.Subscription.Infrastructure.Configuration;

public sealed class SubscriptionCatalogOptions
{
    public ModuleCatalogOptions ModuleCatalog { get; init; } = new();
}

public sealed class ModuleCatalogOptions
{
    public bool SeedOnlyIfEmpty { get; init; } = true;
}
