using System.Text.Json;
using System.Text.Json.Serialization;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Text;

namespace TaxVision.DataCollection.Engine.Definitions;

public sealed class FormDefinition
{
    [JsonRequired]
    public int SchemaVersion { get; init; }
    [JsonRequired]
    public string Title { get; init; } = string.Empty;
    public RichTextDocument? Description { get; init; }
    [JsonRequired]
    public FormPurpose Purpose { get; init; }
    [JsonRequired]
    public FormPresentation Presentation { get; init; }
    public FormSettings Settings { get; init; } = new();
    [JsonRequired]
    public IReadOnlyList<FormPage> Pages { get; init; } = [];
    public IReadOnlyList<FormMapping> Mappings { get; init; } = [];
    public IReadOnlyList<FormAction> Actions { get; init; } = [];
}

public sealed class FormSettings
{
    public bool ShowProgress { get; init; } = true;
    public FormCollectionBasis CollectionBasis { get; init; } = FormCollectionBasis.ServiceRequest;
}

public sealed class FormPage
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;
    public string? Title { get; init; }
    public RichTextDocument? Description { get; init; }
    public Condition? VisibleWhen { get; init; }
    public FormStyle? Style { get; init; }
    [JsonRequired]
    public IReadOnlyList<FormSection> Sections { get; init; } = [];
}

public sealed class FormSection
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;
    public string? Title { get; init; }
    public RichTextDocument? Description { get; init; }
    public Condition? VisibleWhen { get; init; }
    public FormRepeat? Repeat { get; init; }
    public FormStyle? Style { get; init; }
    [JsonRequired]
    public IReadOnlyList<FormComponent> Components { get; init; } = [];
}

public sealed class FormRepeat
{
    public int MinRows { get; init; }
    public int MaxRows { get; init; }
    public string AddLabel { get; init; } = string.Empty;
    public string RowLabel { get; init; } = string.Empty;
}

public sealed class FormComponent
{
    [JsonRequired]
    public string Id { get; init; } = string.Empty;
    [JsonRequired]
    public string Type { get; init; } = string.Empty;
    public string? Key { get; init; }
    public string? Label { get; init; }
    public RichTextDocument? Help { get; init; }
    public bool Required { get; init; }
    public Condition? RequiredWhen { get; init; }
    public Condition? VisibleWhen { get; init; }
    public DataClass? DataClass { get; init; }
    public bool Reportable { get; init; }
    public FormPrefill? Prefill { get; init; }
    public FormStyle? Style { get; init; }
    public JsonElement Props { get; init; }
}

public sealed class FormPrefill
{
    public string Source { get; init; } = string.Empty;
}

public sealed class FormMapping
{
    [JsonRequired]
    public string ComponentId { get; init; } = string.Empty;
    [JsonRequired]
    public string TargetKey { get; init; } = string.Empty;
    public MappingTransform Transform { get; init; }
    public IReadOnlyDictionary<string, string>? OptionMap { get; init; }
    public IReadOnlyDictionary<string, string>? RowFields { get; init; }
}

public sealed class FormAction
{
    [JsonRequired]
    public string Key { get; init; } = string.Empty;
    [JsonRequired]
    public FormActionType Type { get; init; }
    [JsonRequired]
    public FormActionTrigger Trigger { get; init; }
    public Condition? Condition { get; init; }
    public JsonElement Config { get; init; }
}

public sealed class FormStyle
{
    public string? Width { get; init; }
    public string? Align { get; init; }
    public string? TextSize { get; init; }
    public string? TextColor { get; init; }
    public string? Background { get; init; }
    public int? SpacingTop { get; init; }
    public int? SpacingBottom { get; init; }
}
