namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Todas las claves de catálogo que un plan comercial debe traer en su versión publicada (Communication,
/// archivos y firma). Única fuente para el seeder de planes nuevos y para el reconciliador de los existentes.
/// </summary>
public static class PlanEntitlementCatalogs
{
    public static IReadOnlyCollection<EntitlementSeed> SeedsFor(PlanTier tier) =>
        [
            .. CommunicationEntitlementCatalog.SeedsFor(tier),
            .. StorageEntitlementCatalog.SeedsFor(tier),
            .. SignatureEntitlementCatalog.SeedsFor(tier),
        ];

    /// <summary>Semillas que la versión todavía no define (las existentes nunca se tocan).</summary>
    public static IReadOnlyCollection<EntitlementSeed> MissingFrom(SubscriptionPlanVersion version, PlanTier tier)
    {
        var present = version.Entitlements.Select(entitlement => entitlement.Key.Value).ToHashSet(StringComparer.Ordinal);
        return SeedsFor(tier).Where(seed => !present.Contains(seed.Key)).ToArray();
    }
}
