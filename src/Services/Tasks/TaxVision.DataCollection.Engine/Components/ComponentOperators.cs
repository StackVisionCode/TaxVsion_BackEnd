namespace TaxVision.DataCollection.Engine.Components;

internal static class ComponentOperators
{
    internal static readonly string[] Answered = ["isAnswered", "isNotAnswered"];
    internal static readonly string[] Equality = [.. Answered, "equals", "notEquals"];
    internal static readonly string[] Ordered = [.. Equality, "gt", "gte", "lt", "lte"];
    internal static readonly string[] Choice = [.. Equality, "in", "notIn"];
    internal static readonly string[] Boolean = ["isTrue", "isFalse", .. Answered];
    internal static readonly string[] Checkbox = ["isTrue", "isFalse"];
    internal static readonly string[] MultipleChoice = [.. Answered, "contains", "notContains"];
}
