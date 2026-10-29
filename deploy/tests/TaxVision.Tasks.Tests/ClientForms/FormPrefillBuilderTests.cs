using System.Text.Json;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Requests;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormPrefillBuilderTests
{
    [Fact]
    public void Copies_by_component_id_excludes_tax_id_and_file_and_fills_display_name()
    {
        var definition = Def(
            Section(
                "s1",
                null,
                Component("c_first", "text", "first_name"),
                Component("c_tax", "taxId", "ssn"),
                Component("c_file", "file", "docs"),
                Component("c_name", "text", "full_name", new FormPrefill { Source = "customer.displayName" })
            )
        );
        var answers = Answers(
            ("c_first", "\"Ada\""),
            ("c_tax", "{\"sensitiveRef\":\"11111111-1111-1111-1111-111111111111\"}"),
            ("c_file", "[{\"fileId\":\"22222222-2222-2222-2222-222222222222\"}]")
        );

        var (draft, prefilled) = FormPrefillBuilder.Build(definition, answers, definition, "Ada Lovelace");

        Assert.Equal("Ada", draft.Values["c_first"].GetString());
        Assert.Equal("Ada Lovelace", draft.Values["c_name"].GetString());
        Assert.False(draft.Values.ContainsKey("c_tax"));
        Assert.False(draft.Values.ContainsKey("c_file"));
        Assert.Contains("c_first", prefilled);
        Assert.Contains("c_name", prefilled);
        Assert.DoesNotContain("c_tax", prefilled);
        Assert.DoesNotContain("c_file", prefilled);
    }

    [Fact]
    public void Falls_back_to_key_when_component_id_differs()
    {
        var source = Def(Section("s1", null, Component("old_id", "text", "first_name")));
        var answers = Answers(("old_id", "\"Ada\""));
        var target = Def(Section("s1", null, Component("new_id", "text", "first_name")));

        var (draft, prefilled) = FormPrefillBuilder.Build(source, answers, target, null);

        Assert.Equal("Ada", draft.Values["new_id"].GetString());
        Assert.Contains("new_id", prefilled);
    }

    [Fact]
    public void Repeater_rows_get_new_row_ids()
    {
        var def = Def(
            Section("dep", new FormRepeat { MinRows = 0, MaxRows = 5 }, Component("d_first", "text", "first_name"))
        );
        var answers = Answers(("dep", "{\"rows\":[{\"rowId\":\"r1\",\"values\":{\"d_first\":\"Kid\"}}]}"));

        var (draft, prefilled) = FormPrefillBuilder.Build(def, answers, def, null);

        Assert.Contains("dep", prefilled);
        var rows = draft.Values["dep"].GetProperty("rows");
        var row = Assert.Single(rows.EnumerateArray());
        Assert.NotEqual("r1", row.GetProperty("rowId").GetString());
        Assert.Equal("Kid", row.GetProperty("values").GetProperty("d_first").GetString());
    }

    private static FormComponent Component(string id, string type, string? key, FormPrefill? prefill = null) =>
        new()
        {
            Id = id,
            Type = type,
            Key = key,
            Prefill = prefill,
        };

    private static FormSection Section(string id, FormRepeat? repeat, params FormComponent[] components) =>
        new()
        {
            Id = id,
            Repeat = repeat,
            Components = components,
        };

    private static FormDefinition Def(params FormSection[] sections) =>
        new()
        {
            SchemaVersion = 1,
            Title = "Test",
            Purpose = FormPurpose.General,
            Presentation = FormPresentation.SinglePage,
            Pages = [new FormPage { Id = "p1", Sections = sections }],
        };

    private static FormAnswers Answers(params (string Key, string Json)[] values)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, json) in values)
            map[key] = JsonDocument.Parse(json).RootElement.Clone();
        return new FormAnswers { SchemaVersion = 1, Values = map };
    }
}
