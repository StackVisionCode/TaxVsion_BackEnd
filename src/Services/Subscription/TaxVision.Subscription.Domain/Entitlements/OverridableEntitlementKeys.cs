using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Entitlements;

/// <summary>
/// Qué claves admiten una excepción por oficina y de qué tipo es cada una. El tipo NO lo elige quien da la
/// excepción: sale del mismo catálogo que usan los planes, así una excepción nunca puede convertir un
/// número en texto y romper a los consumidores (CloudStorage, Auth, Communication).
/// <list type="bullet">
/// <item>Comerciales: <c>seats.max</c>, <c>storage.max_bytes</c>, <c>invitations.max_pending</c>.</item>
/// <item>De plataforma: las del catálogo de defaults (<c>communication.*</c>, <c>storage.*</c>, firma, finanzas).</item>
/// <item>Módulos vendibles: <c>module.{código}</c> como Bool. Los de núcleo no: siempre están encendidos.</item>
/// </list>
/// </summary>
public static class OverridableEntitlementKeys
{
    private const string ModulePrefix = "module.";

    private static readonly string[] CommercialKeys =
    [
        CommercialTerms.SeatsMaxKey,
        CommercialTerms.StorageMaxBytesKey,
        CommercialTerms.InvitationsMaxPendingKey,
    ];

    public static Result<(EntitlementKey Key, EntitlementValueType ValueType)> Resolve(
        string key,
        IReadOnlyCollection<Module> moduleCatalog
    )
    {
        var parsed = EntitlementKey.Create(key?.Trim() ?? string.Empty);
        if (parsed.IsFailure)
            return NotOverridable(key);

        var value = parsed.Value.Value;
        if (CommercialKeys.Contains(value, StringComparer.Ordinal))
            return Found(parsed.Value, CommercialTerms.TypeOf(value));

        if (EntitlementKeyCatalog.PlatformDefaults.FirstOrDefault(seed => seed.Key == value) is { } seed)
            return Found(parsed.Value, seed.ValueType);

        if (value.StartsWith(ModulePrefix, StringComparison.Ordinal))
        {
            var code = value[ModulePrefix.Length..];
            var module = moduleCatalog.FirstOrDefault(m =>
                string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase)
            );
            if (module is { Category: ModuleCategory.Commercial })
                return Found(parsed.Value, EntitlementValueType.Bool);
        }

        return NotOverridable(key);
    }

    private static Result<(EntitlementKey Key, EntitlementValueType ValueType)> Found(
        EntitlementKey key,
        EntitlementValueType valueType
    ) => Result.Success((key, valueType));

    private static Result<(EntitlementKey Key, EntitlementValueType ValueType)> NotOverridable(string? key) =>
        Result.Failure<(EntitlementKey Key, EntitlementValueType ValueType)>(
            new Error(
                "EntitlementOverride.KeyNotOverridable",
                $"'{key}' is not a limit that can be set per office (unknown key or core module)."
            )
        );
}
