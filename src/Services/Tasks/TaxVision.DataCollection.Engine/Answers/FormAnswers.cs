using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaxVision.DataCollection.Engine.Answers;

public sealed class FormAnswers
{
    [JsonRequired]
    public int SchemaVersion { get; init; }
    [JsonRequired]
    public IReadOnlyDictionary<string, JsonElement> Values { get; init; } = new Dictionary<string, JsonElement>();

    public FormAnswers WithValues(IReadOnlyDictionary<string, JsonElement> values) =>
        new() { SchemaVersion = SchemaVersion, Values = values };
}

public sealed class RepeaterRows
{
    public IReadOnlyList<RepeaterRow> Rows { get; init; } = [];
}

public sealed class RepeaterRow
{
    public string RowId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, JsonElement> Values { get; init; } = new Dictionary<string, JsonElement>();
}
