using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Application.AddOns;

/// <summary>
/// Cuándo tiene sentido comprar un add-on. Lo comparten el read model del Account (que pinta el estado) y
/// la compra (que la rechaza), para que la pantalla y el backend nunca digan cosas distintas.
/// </summary>
public static class AddOnEligibilityRules
{
    /// <summary>Prefijo de las features que habilitan un módulo, igual que en el catálogo de planes.</summary>
    private const string ModuleFeaturePrefix = "module.";

    /// <summary>Módulos que aporta el add-on.</summary>
    public static IReadOnlyList<string> ModulesOf(AddOnDefinition definition) =>
        definition
            .Features.Where(feature =>
                feature.Enabled && feature.FeatureKey.Value.StartsWith(ModuleFeaturePrefix, StringComparison.Ordinal)
            )
            .Select(feature => feature.FeatureKey.Value[ModuleFeaturePrefix.Length..])
            .ToList();

    /// <summary>
    /// Incluido cuando el plan ya habilita TODOS los módulos que el add-on aporta: comprarlo no daría nada
    /// nuevo. Un add-on sin módulos (p. ej. solo cupos) nunca se da por incluido.
    /// </summary>
    public static bool IsIncludedInPlan(AddOnDefinition definition, IReadOnlyList<string> enabledModules)
    {
        var modules = ModulesOf(definition);
        return modules.Count > 0
            && modules.All(module => enabledModules.Contains(module, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ¿Ya lo tiene? Solo cuenta lo que está vigente; un add-on que admite varias instancias se puede repetir.
    /// </summary>
    public static bool AlreadyOwned(AddOnDefinition definition, IReadOnlyList<TenantAddOn> owned) =>
        !definition.AllowMultipleInstances
        && owned.Any(addOn =>
            addOn.Status == AddOnStatus.Active
            && string.Equals(addOn.AddOnCode, definition.Code.Value, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>
    /// Hay otro add-on activo del mismo <c>MutexGroup</c> (p. ej. otro tramo de storage) que impide esta compra.
    /// Lo evalúa igual que el guard de compra (<c>AddOnPurchaseEligibility</c>) para que read-model y write-side
    /// nunca digan cosas distintas: la pantalla no debe pintar comprable algo que el POST va a rechazar.
    /// El lookup de MutexGroup por código se calcula una vez por request con <c>MutexGroupLookupFor</c>.
    /// </summary>
    public static bool BlockedByMutex(
        AddOnDefinition definition,
        IReadOnlyList<TenantAddOn> owned,
        IReadOnlyDictionary<string, string?> mutexGroupByCode
    )
    {
        if (definition.MutexGroup is null)
            return false;
        foreach (var ownedAddOn in owned)
        {
            if (ownedAddOn.Status != AddOnStatus.Active)
                continue;
            if (string.Equals(ownedAddOn.AddOnCode, definition.Code.Value, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!mutexGroupByCode.TryGetValue(ownedAddOn.AddOnCode, out var ownedGroup) || ownedGroup is null)
                continue;
            if (string.Equals(ownedGroup, definition.MutexGroup, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Diccionario <c>Code → MutexGroup</c> del catálogo para resolver los <c>owned</c> sin re-consultar.</summary>
    public static IReadOnlyDictionary<string, string?> MutexGroupLookupFor(IEnumerable<AddOnDefinition> definitions) =>
        definitions.ToDictionary(d => d.Code.Value, d => d.MutexGroup, StringComparer.OrdinalIgnoreCase);
}
