using System.Globalization;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Entitlements;

/// <summary>
/// Combina el valor de un entitlement heredado del plan con el valor aportado por un
/// add-on según su <see cref="AddOnMergeStrategy"/>. Vive en Application (no en Domain)
/// porque opera sobre datos ya proyectados para el snapshot, no sobre un aggregate.
/// </summary>
public static class EntitlementMerger
{
    public static EntitlementEntry MergeAddOnValue(
        EntitlementEntry? existing, EntitlementKey key, EntitlementValueType valueType, string incomingValue, AddOnMergeStrategy strategy)
    {
        if (existing is null)
            return new EntitlementEntry(key, valueType, incomingValue, EntitlementStatus.Active, EntitlementSource.AddOn, ExpiresAtUtc: null);

        var mergedValue = strategy switch
        {
            AddOnMergeStrategy.Replace => incomingValue,
            AddOnMergeStrategy.Or => MergeBool(existing.Value, incomingValue),
            AddOnMergeStrategy.Max => MergeNumeric(existing.Value, incomingValue, valueType, Math.Max),
            AddOnMergeStrategy.Sum => MergeNumeric(existing.Value, incomingValue, valueType, (a, b) => a + b),
            _ => incomingValue,
        };

        return existing with { Value = mergedValue, Source = EntitlementSource.AddOn };
    }

    private static string MergeBool(string existingValue, string incomingValue)
    {
        var existingBool = bool.TryParse(existingValue, out var e) && e;
        var incomingBool = bool.TryParse(incomingValue, out var i) && i;
        return (existingBool || incomingBool).ToString();
    }

    private static string MergeNumeric(string existingValue, string incomingValue, EntitlementValueType valueType, Func<decimal, decimal, decimal> combine)
    {
        var existingNumber = decimal.TryParse(existingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var e) ? e : 0m;
        var incomingNumber = decimal.TryParse(incomingValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var i) ? i : 0m;
        var result = combine(existingNumber, incomingNumber);

        return valueType switch
        {
            EntitlementValueType.Int => ((int)result).ToString(CultureInfo.InvariantCulture),
            EntitlementValueType.Long => ((long)result).ToString(CultureInfo.InvariantCulture),
            _ => result.ToString(CultureInfo.InvariantCulture),
        };
    }
}
