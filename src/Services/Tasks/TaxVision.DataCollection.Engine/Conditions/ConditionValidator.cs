using System.Text.Json;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Formats;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Conditions;

internal sealed class ConditionValidator(DataCollectionLimits limits)
{
    private int _nodes;

    internal void Validate(
        Condition? condition,
        string path,
        int currentOrder,
        string? currentRepeater,
        DefinitionIndex index,
        ICollection<ValidationIssue> issues
    )
    {
        if (condition is null)
            return;
        _nodes = 0;
        ValidateNode(condition, path, currentOrder, currentRepeater, index, issues, 1);
    }

    private void ValidateNode(
        Condition condition,
        string path,
        int currentOrder,
        string? currentRepeater,
        DefinitionIndex index,
        ICollection<ValidationIssue> issues,
        int depth
    )
    {
        _nodes++;
        if (_nodes > limits.MaxConditionNodes)
        {
            Add(issues, "condition.too_many_nodes", path, "Condition contains too many nodes.");
            return;
        }
        if (depth > limits.MaxConditionDepth)
        {
            Add(issues, "condition.too_deep", path, "Condition is too deeply nested.");
            return;
        }

        var shapes =
            (condition.All is null ? 0 : 1)
            + (condition.Any is null ? 0 : 1)
            + (condition.Not is null ? 0 : 1)
            + (condition.Field is null ? 0 : 1)
            + (condition.Rows is null ? 0 : 1);
        if (shapes != 1)
        {
            Add(issues, "condition.value_invalid", path, "Condition must contain exactly one expression shape.");
            return;
        }

        if (condition.All is not null || condition.Any is not null)
        {
            if (condition.Op is not null || condition.Value.ValueKind != JsonValueKind.Undefined)
            {
                Add(issues, "condition.value_invalid", path, "Condition group contains unsupported properties.");
                return;
            }
            var children = condition.All ?? condition.Any!;
            if (children.Count is < 1 or > 10)
                Add(issues, "condition.value_invalid", path, "Condition group must contain between 1 and 10 children.");
            for (var indexValue = 0; indexValue < children.Count; indexValue++)
                ValidateNode(
                    children[indexValue],
                    $"{path}/{(condition.All is null ? "any" : "all")}/{indexValue}",
                    currentOrder,
                    currentRepeater,
                    index,
                    issues,
                    depth + 1
                );
            return;
        }

        if (condition.Not is not null)
        {
            if (condition.Op is not null || condition.Value.ValueKind != JsonValueKind.Undefined)
            {
                Add(issues, "condition.value_invalid", path, "Negated condition contains unsupported properties.");
                return;
            }
            ValidateNode(condition.Not, $"{path}/not", currentOrder, currentRepeater, index, issues, depth + 1);
            return;
        }

        if (condition.Rows is not null)
        {
            ValidateRows(condition, path, currentOrder, index, issues);
            return;
        }

        ValidateField(condition, path, currentOrder, currentRepeater, index, issues);
    }

    private void ValidateRows(
        Condition condition,
        string path,
        int currentOrder,
        DefinitionIndex index,
        ICollection<ValidationIssue> issues
    )
    {
        if (!index.Sections.TryGetValue(condition.Rows!, out var target) || target.Section.Repeat is null)
        {
            Add(issues, "condition.unknown_field", $"{path}/rows", "Repeater section was not found.");
            return;
        }
        if (target.Order >= currentOrder)
            Add(
                issues,
                "condition.forward_reference",
                $"{path}/rows",
                "Conditions can reference only earlier repeaters."
            );
        if (condition.Op is not ("countGte" or "countLte" or "countEquals"))
            Add(issues, "condition.op_not_allowed", $"{path}/op", "Operator is not valid for repeater rows.");
        if (
            condition.Value.ValueKind != JsonValueKind.Number
            || !condition.Value.TryGetInt32(out var count)
            || count < 0
            || count > limits.MaxRepeaterRows
        )
            Add(issues, "condition.value_invalid", $"{path}/value", "Row count is outside the platform limit.");
    }

    private static void ValidateField(
        Condition condition,
        string path,
        int currentOrder,
        string? currentRepeater,
        DefinitionIndex index,
        ICollection<ValidationIssue> issues
    )
    {
        if (
            !index.Components.TryGetValue(condition.Field!, out var target)
            || target.ComponentType?.Kind != ComponentKind.Input
        )
        {
            Add(
                issues,
                "condition.unknown_field",
                $"{path}/field",
                "Condition field was not found or is not an input."
            );
            return;
        }
        if (target.Order >= currentOrder)
            Add(
                issues,
                "condition.forward_reference",
                $"{path}/field",
                "Conditions can reference only earlier fields."
            );
        if (target.RepeaterSectionId is not null && target.RepeaterSectionId != currentRepeater)
            Add(
                issues,
                "condition.out_of_scope",
                $"{path}/field",
                "Repeater fields are visible only inside the same row."
            );
        var operatorAllowed = condition.Op is not null && target.ComponentType.AllowedOperators.Contains(condition.Op);
        if (!operatorAllowed)
            Add(issues, "condition.op_not_allowed", $"{path}/op", "Operator is not valid for the referenced field.");
        ValidateValue(condition, target.Component, operatorAllowed, path, issues);
    }

    private static void ValidateValue(
        Condition condition,
        FormComponent target,
        bool operatorAllowed,
        string path,
        ICollection<ValidationIssue> issues
    )
    {
        var noValue = condition.Op is "isAnswered" or "isNotAnswered" or "isTrue" or "isFalse";
        if (noValue)
        {
            if (condition.Value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
                Add(issues, "condition.value_invalid", $"{path}/value", "This operator does not accept a value.");
            return;
        }
        if (condition.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            Add(issues, "condition.value_invalid", $"{path}/value", "This operator requires a value.");
            return;
        }
        if (
            condition.Op is "in" or "notIn"
            && (condition.Value.ValueKind != JsonValueKind.Array || condition.Value.GetArrayLength() is < 1 or > 50)
        )
        {
            Add(issues, "condition.value_invalid", $"{path}/value", "Set comparison requires 1 to 50 values.");
            return;
        }
        // Con un operador no permitido ya hay un issue; el tipo del valor no se puede juzgar.
        if (!operatorAllowed)
            return;
        var options = JsonProps.OptionValues(target.Props);
        var valid = condition.Op switch
        {
            "in" or "notIn" => condition.Value.EnumerateArray().All(item => IsOption(options, item)),
            "contains" or "notContains" => IsOption(options, condition.Value),
            _ => MatchesFieldType(target.Type, condition.Value),
        };
        if (!valid)
            Add(issues, "condition.value_invalid", $"{path}/value", "Condition value does not match the field type.");
    }

    private static bool IsOption(IReadOnlySet<string> options, JsonElement value) =>
        value.ValueKind == JsonValueKind.String && options.Contains(value.GetString()!);

    private static bool MatchesFieldType(string type, JsonElement value) =>
        type switch
        {
            "number" or "currency" => PlainNumberFormat.TryRead(value, out _),
            "date" => value.ValueKind == JsonValueKind.String && DateFormat.TryParse(value.GetString(), out _),
            "yesNo" or "checkbox" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => value.ValueKind == JsonValueKind.String,
        };

    private static void Add(ICollection<ValidationIssue> issues, string code, string path, string message) =>
        issues.Add(new ValidationIssue(code, path, null, null, message));
}
