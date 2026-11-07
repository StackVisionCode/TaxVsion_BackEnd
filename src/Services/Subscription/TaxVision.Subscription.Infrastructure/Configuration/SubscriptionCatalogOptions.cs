namespace TaxVision.Subscription.Infrastructure.Configuration;

public sealed class SubscriptionCatalogOptions
{
    public ModuleCatalogOptions ModuleCatalog { get; init; } = new();

    /// <summary>Candado de la reconciliación al arrancar: lo que una réplica puede tardar antes de que otra entre.</summary>
    public TimeSpan ReconcileLockTtl { get; init; } = TimeSpan.FromMinutes(10);
}

public sealed class ModuleCatalogOptions
{
    public bool SeedOnlyIfEmpty { get; init; } = true;
}
