using System.Globalization;
using System.Text.Json;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
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
        return EvaluateField(condition, answers, index, visibility, rowId, rowValues);
    }

    private static bool EvaluateRows(Condition condition, FormAnswers answers, VisibilityMap visibility)
    {
        var count = visibility.IsVisible(condition.Rows!) ? AnswersReader.ReadRows(answers, condition.Rows!).Count : 0;
        var expected = condition.Value.TryGetInt32(out var parsed) ? parsed : 0;
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

        return condition.Op switch
        {
            "isAnswered" => answered,
            "isNotAnswered" => !answered,
            "isTrue" => answered && value.ValueKind == JsonValueKind.True,
            "isFalse" => answered && value.ValueKind == JsonValueKind.False,
            _ when !answered => false,
            "equals" => Compare(value, condition.Value, target.Component.Type) == 0,
            "notEquals" => Compare(value, condition.Value, target.Component.Type) != 0,
            "gt" => Compare(value, condition.Value, target.Component.Type) > 0,
            "gte" => Compare(value, condition.Value, target.Component.Type) >= 0,
            "lt" => Compare(value, condition.Value, target.Component.Type) < 0,
            "lte" => Compare(value, condition.Value, target.Component.Type) <= 0,
            "in" => condition.Value.ValueKind == JsonValueKind.Array && condition.Value.EnumerateArray().Any(item => Compare(value, item, target.Component.Type) == 0),
            "notIn" => condition.Value.ValueKind == JsonValueKind.Array && condition.Value.EnumerateArray().All(item => Compare(value, item, target.Component.Type) != 0),
            "contains" => Contains(value, condition.Value),
            "notContains" => !Contains(value, condition.Value),
            _ => false,
        };
    }

    private static int Compare(JsonElement left, JsonElement right, string type)
    {
        if (type is "number" or "currency"
            && decimal.TryParse(left.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var leftNumber)
            && decimal.TryParse(right.ValueKind == JsonValueKind.String ? right.GetString() : right.GetRawText(), NumberStyles.Number, CultureInfo.InvariantCulture, out var rightNumber))
            return leftNumber.CompareTo(rightNumber);
        if (type == "date"
            && DateOnly.TryParseExact(left.GetString(), "yyyy-MM-dd", out var leftDate)
            && DateOnly.TryParseExact(right.GetString(), "yyyy-MM-dd", out var rightDate))
            return leftDate.CompareTo(rightDate);

        var leftText = left.ValueKind == JsonValueKind.String ? left.GetString() : left.GetRawText();
        var rightText = right.ValueKind == JsonValueKind.String ? right.GetString() : right.GetRawText();
        return string.Compare(leftText, rightText, type == "email" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool Contains(JsonElement value, JsonElement expected) =>
        value.ValueKind == JsonValueKind.Array
        && value.EnumerateArray().Any(item => string.Equals(item.GetString(), expected.GetString(), StringComparison.Ordinal));
}
