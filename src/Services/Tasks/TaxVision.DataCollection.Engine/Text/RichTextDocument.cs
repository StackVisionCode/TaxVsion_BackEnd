using System.Text.Json.Serialization;

namespace TaxVision.DataCollection.Engine.Text;

public sealed class RichTextDocument
{
    [JsonRequired]
    public string Type { get; init; } = string.Empty;

    [JsonRequired]
    public IReadOnlyList<RichTextBlock> Blocks { get; init; } = [];
}

public sealed class RichTextBlock
{
    [JsonRequired]
    public string Type { get; init; } = string.Empty;
    public int? Level { get; init; }
    public string? Align { get; init; }
    public string? Font { get; init; }
    public IReadOnlyList<RichTextInline> Children { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<RichTextInline>> Items { get; init; } = [];
}

public sealed class RichTextInline
{
    [JsonRequired]
    public string Type { get; init; } = string.Empty;
    public string? Text { get; init; }
    public string? Href { get; init; }
    public IReadOnlyList<string> Marks { get; init; } = [];
    public string? Color { get; init; }
    public IReadOnlyList<RichTextInline> Children { get; init; } = [];
}
