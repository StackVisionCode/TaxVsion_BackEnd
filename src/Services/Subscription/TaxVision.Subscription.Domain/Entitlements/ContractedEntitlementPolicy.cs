using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Entitlements;

/// <summary>
/// D4: qué le corresponde a un cliente por su plan. Sale de la edición que firmó, nunca de la publicada, más las
/// reglas de plataforma:
/// <list type="bullet">
/// <item>los límites comerciales y los módulos vendibles, los de su edición;</item>
/// <item>los módulos base del catálogo, siempre;</item>
/// <item>los módulos que ya no están en el catálogo, nunca;</item>
/// <item>las claves de plataforma que su edición no define, con el valor de <see cref="EntitlementKeyCatalog"/>.</item>
/// </list>
/// La foto de derechos, la pantalla del plan y la venta de add-ons usan esta misma regla.
/// </summary>
public static class ContractedEntitlementPolicy
{
    private const string ModulePrefix = "module.";

    private static readonly Error ModuleCatalogUnavailable = new(
        "Entitlements.ModuleCatalogUnavailable",
        "The module catalog is empty; entitlements cannot be decided."
    );

    /// <summary>Entradas de la foto antes de add-ons. Sin acceso, todo módulo queda apagado; los límites siguen.</summary>
    public static Result<Dictionary<string, EntitlementEntry>> BaseEntries(
        SubscriptionPlanVersion contracted,
        IReadOnlyCollection<Module> catalog,
        bool grantsAccess
    )
    {
        // Sin catálogo no se sabe qué es base ni qué se retiró: mejor fallar que apagarle todo al cliente.
        if (catalog.Count == 0)
            return Result.Failure<Dictionary<string, EntitlementEntry>>(ModuleCatalogUnavailable);

        var entries = new Dictionary<string, EntitlementEntry>(StringComparer.Ordinal);

        foreach (var entitlement in contracted.Entitlements)
            entries[entitlement.Key.Value] = Entry(entitlement.Key, entitlement.ValueType, entitlement.DefaultValue);

        foreach (var seed in EntitlementKeyCatalog.MissingFrom(contracted))
            entries[seed.Key] = Entry(EntitlementKey.Create(seed.Key).Value, seed.ValueType, seed.DefaultValue);

        var known = catalog.Select(module => module.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in contracted.Features)
        {
            var key = feature.FeatureKey.Value;
            if (!key.StartsWith(ModulePrefix, StringComparison.Ordinal))
            {
                entries[key] = Entry(feature.FeatureKey, EntitlementValueType.Bool, feature.DefaultEnabled.ToString());
                continue;
            }

            if (known.Contains(key[ModulePrefix.Length..]))
                entries[key] = Module(feature.FeatureKey, feature.DefaultEnabled, grantsAccess);
        }

        foreach (var core in catalog.Where(module => module.Category == ModuleCategory.Core))
        {
            var key = EntitlementKey.Create(ModulePrefix + core.Code).Value;
            entries[key.Value] = Module(key, enabled: true, grantsAccess);
        }

        return Result.Success(entries);
    }

    /// <summary>Códigos de módulo encendidos por el plan (sin add-ons), ordenados.</summary>
    public static Result<IReadOnlyList<string>> EnabledModules(
        SubscriptionPlanVersion contracted,
        IReadOnlyCollection<Module> catalog
    )
    {
        if (catalog.Count == 0)
            return Result.Failure<IReadOnlyList<string>>(ModuleCatalogUnavailable);

        var known = catalog.Select(module => module.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contractedModules = contracted
            .Features.Where(feature =>
                feature.DefaultEnabled && feature.FeatureKey.Value.StartsWith(ModulePrefix, StringComparison.Ordinal)
            )
            .Select(feature => feature.FeatureKey.Value[ModulePrefix.Length..])
            .Where(known.Contains);
        var core = catalog.Where(module => module.Category == ModuleCategory.Core).Select(module => module.Code);

        return Result.Success<IReadOnlyList<string>>(
            contractedModules
                .Concat(core)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToArray()
        );
    }

    private static EntitlementEntry Entry(EntitlementKey key, EntitlementValueType type, string value) =>
        new(key, type, value, EntitlementStatus.Active, EntitlementSource.Plan, ExpiresAtUtc: null);

    // Un módulo solo se enciende si el plan lo trae y la base da acceso; el gate lee el valor.
    private static EntitlementEntry Module(EntitlementKey key, bool enabled, bool grantsAccess) =>
        new(
            key,
            EntitlementValueType.Bool,
            (grantsAccess && enabled).ToString(),
            grantsAccess ? EntitlementStatus.Active : EntitlementStatus.Disabled,
            EntitlementSource.Plan,
            ExpiresAtUtc: null
        );
}
