using System.Text.Json;

namespace TaxVision.DataCollection.Engine.Conditions;

public sealed class Condition
{
    public IReadOnlyList<Condition>? All { get; init; }
    public IReadOnlyList<Condition>? Any { get; init; }
    public Condition? Not { get; init; }
    public string? Field { get; init; }
    public string? Rows { get; init; }
    public string? Op { get; init; }
    public JsonElement Value { get; init; }
}
