using System.Text.Json;
using TaxVision.DataCollection.Engine.Conditions;

namespace TaxVision.DataCollection.Engine.Mapping;

// Decide si una condición se cumple siempre que el discriminador (p. ej. el tipo de cliente) cae en ciertos valores.
internal sealed class KindConditionAnalyzer
{
    private readonly string? _discriminatorComponentId;
    private readonly IReadOnlyDictionary<string, string> _optionKinds;

    internal KindConditionAnalyzer(string? discriminatorComponentId, IReadOnlyDictionary<string, string>? optionKinds)
    {
        _discriminatorComponentId = discriminatorComponentId;
        _optionKinds = optionKinds ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    internal bool HoldsFor(Condition? condition, IReadOnlySet<string> kinds)
    {
        if (condition is null)
            return true;
        if (_discriminatorComponentId is null || kinds.Count == 0)
            return false;
        var options = _optionKinds.Where(pair => kinds.Contains(pair.Value)).Select(pair => pair.Key).ToArray();
        return options.Length > 0 && options.All(option => Evaluate(condition, option) == true);
    }

    // Null cuando la condición depende de algo más que el discriminador.
    private bool? Evaluate(Condition condition, string option)
    {
        if (condition.All is not null)
            return Combine(condition.All.Select(child => Evaluate(child, option)).ToArray(), requireAll: true);
        if (condition.Any is not null)
            return Combine(condition.Any.Select(child => Evaluate(child, option)).ToArray(), requireAll: false);
        if (condition.Not is not null)
            return Evaluate(condition.Not, option) is bool inner ? !inner : null;
        if (condition.Field != _discriminatorComponentId)
            return null;
        return condition.Op switch
        {
            "equals" => Matches(condition.Value, option),
            "notEquals" => Negate(Matches(condition.Value, option)),
            "in" => Contains(condition.Value, option),
            "notIn" => Negate(Contains(condition.Value, option)),
            "isAnswered" => true,
            "isNotAnswered" => false,
            _ => null,
        };
    }

    private static bool? Combine(IReadOnlyList<bool?> results, bool requireAll)
    {
        if (requireAll)
            return results.Contains(false) ? false
                : results.All(result => result == true) ? true
                : null;
        return results.Contains(true) ? true
            : results.All(result => result == false) ? false
            : null;
    }

    private static bool? Matches(JsonElement value, string option) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() == option : null;

    private static bool? Contains(JsonElement value, string option)
    {
        if (value.ValueKind != JsonValueKind.Array)
            return null;
        var found = false;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return null;
            found |= item.GetString() == option;
        }
        return found;
    }

    private static bool? Negate(bool? value) => value is bool known ? !known : null;
}
