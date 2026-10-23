using System.Text.Json;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Formats;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.DataCollection.Engine.Visibility;

namespace TaxVision.DataCollection.Engine.Conditions;

internal static class ConditionEvaluator
{
    internal static bool Evaluate(
        Condition? condition,
        FormAnswers answers,
        DefinitionIndex index,
        VisibilityMap visibility,
        string? rowId = null,
        IReadOnlyDictionary<string, JsonElement>? rowValues = null
    )
    {
        if (condition is null)
            return true;
        if (condition.All is not null)
            return condition.All.All(item => Evaluate(item, answers, index, visibility, rowId, rowValues));
        if (condition.Any is not null)
            return condition.Any.Any(item => Evaluate(item, answers, index, visibility, rowId, rowValues));
        if (condition.Not is not null)
            return !Evaluate(condition.Not, answers, index, visibility, rowId, rowValues);
        if (condition.Rows is not null)
            return EvaluateRows(condition, answers, visibility);
        return condition.Field is not null && EvaluateField(condition, answers, index, visibility, rowId, rowValues);
    }

    private static bool EvaluateRows(Condition condition, FormAnswers answers, VisibilityMap visibility)
    {
        if (condition.Value.ValueKind != JsonValueKind.Number || !condition.Value.TryGetInt32(out var expected))
            return false;
        var count = visibility.RowCount(condition.Rows!, answers);
        return condition.Op switch
        {
            "countGte" => count >= expected,
            "countLte" => count <= expected,
            "countEquals" => count == expected,
            _ => false,
        };
    }

    private static bool EvaluateField(
        Condition condition,
        FormAnswers answers,
        DefinitionIndex index,
        VisibilityMap visibility,
        string? rowId,
        IReadOnlyDictionary<string, JsonElement>? rowValues
    )
    {
        if (!index.Components.TryGetValue(condition.Field!, out var target))
            return false;

        var isVisible = target.RepeaterSectionId is null
            ? visibility.IsVisible(target.Component.Id)
            : rowId is not null && visibility.IsVisible(target.Component.Id, rowId);
        var value = isVisible
            ? target.RepeaterSectionId is null
                ? AnswersReader.Get(answers.Values, target.Component.Id)
                : AnswersReader.Get(rowValues, target.Component.Id)
            : default;
        var answered = AnswersReader.IsAnswered(value, target.Component.Type);
        var type = target.Component.Type;

        // Un tipo inesperado en la respuesta o en el valor no es comparable: la condición da false.
        return condition.Op switch
        {
            "isAnswered" => answered,
            "isNotAnswered" => !answered,
            "isTrue" => answered && value.ValueKind == JsonValueKind.True,
            "isFalse" => answered && value.ValueKind == JsonValueKind.False,
            _ when !answered => false,
            "equals" => Compare(value, condition.Value, type) == 0,
            "notEquals" => Compare(value, condition.Value, type) is { } notEqual && notEqual != 0,
            "gt" => Compare(value, condition.Value, type) > 0,
            "gte" => Compare(value, condition.Value, type) >= 0,
            "lt" => Compare(value, condition.Value, type) < 0,
            "lte" => Compare(value, condition.Value, type) <= 0,
            "in" => condition.Value.ValueKind == JsonValueKind.Array
                && condition.Value.EnumerateArray().Any(item => Compare(value, item, type) == 0),
            "notIn" => condition.Value.ValueKind == JsonValueKind.Array
                && condition.Value.EnumerateArray().All(item => Compare(value, item, type) is { } other && other != 0),
            "contains" => Contains(value, condition.Value) == true,
            "notContains" => Contains(value, condition.Value) == false,
            _ => false,
        };
    }

    private static int? Compare(JsonElement left, JsonElement right, string type)
    {
        if (type is "number" or "currency")
            return
                left.ValueKind == JsonValueKind.String
                && PlainNumberFormat.TryParse(left.GetString(), out double leftNumber)
                && PlainNumberFormat.TryRead(right, out var rightNumber)
                ? leftNumber.CompareTo(rightNumber)
                : null;
        if (type == "date")
            return
                left.ValueKind == JsonValueKind.String
                && right.ValueKind == JsonValueKind.String
                && DateFormat.TryParse(left.GetString(), out var leftDate)
                && DateFormat.TryParse(right.GetString(), out var rightDate)
                ? leftDate.CompareTo(rightDate)
                : null;
        if (left.ValueKind == JsonValueKind.String && right.ValueKind == JsonValueKind.String)
            return string.Compare(
                left.GetString(),
                right.GetString(),
                type == "email" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
            );
        if (
            (left.ValueKind is JsonValueKind.True or JsonValueKind.False)
            && (right.ValueKind is JsonValueKind.True or JsonValueKind.False)
        )
            return left.ValueKind == right.ValueKind ? 0 : 1;
        return null;
    }

    // null cuando la respuesta o el valor no son strings: ni contains ni notContains se cumplen.
    private static bool? Contains(JsonElement value, JsonElement expected)
    {
        if (value.ValueKind != JsonValueKind.Array || expected.ValueKind != JsonValueKind.String)
            return null;
        var found = false;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return null;
            found |= string.Equals(item.GetString(), expected.GetString(), StringComparison.Ordinal);
        }
        return found;
    }
}
