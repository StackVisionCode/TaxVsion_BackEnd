namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Claves de plataforma (comunicación, archivos y firma) con un solo valor por defecto cada una. Solo rellenan lo
/// que a una edición le falta; una clave que la edición ya define nunca se pisa. Las claves comerciales
/// (asientos, almacenamiento, invitaciones y módulos) no están acá: las fija cada edición.
/// </summary>
public static class EntitlementKeyCatalog
{
    public static IReadOnlyCollection<EntitlementSeed> PlatformDefaults { get; } =
    [
        .. CommunicationEntitlementCatalog.Defaults,
        .. StorageEntitlementCatalog.Defaults,
        .. SignatureEntitlementCatalog.Defaults,
        .. FinancialInsightsEntitlementCatalog.Defaults,
    ];

    /// <summary>Claves de plataforma que la versión todavía no define.</summary>
    public static IReadOnlyCollection<EntitlementSeed> MissingFrom(SubscriptionPlanVersion version)
    {
        var present = version
            .Entitlements.Select(entitlement => entitlement.Key.Value)
            .ToHashSet(StringComparer.Ordinal);
        return PlatformDefaults.Where(seed => !present.Contains(seed.Key)).ToArray();
    }
}
