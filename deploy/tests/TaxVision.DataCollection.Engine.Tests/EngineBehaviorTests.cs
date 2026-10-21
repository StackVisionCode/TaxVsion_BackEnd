using System.Text.Json;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class EngineBehaviorTests
{
    [Fact]
    public void Hidden_answers_are_preserved_in_draft_and_removed_for_submit()
    {
        var definition = ConditionalDefinition();
        var answers = Answers(new Dictionary<string, object>
        {
            ["fd_toggle000"] = false,
            ["fd_detail000"] = "keep while drafting",
        });
        var engine = EngineTestFactory.Create();

        var visibility = engine.EvaluateVisibility(definition, answers);
        var sanitized = engine.RemoveHiddenAnswers(definition, answers, visibility);

        Assert.False(visibility.IsVisible("fd_detail000"));
        Assert.True(answers.Values.ContainsKey("fd_detail000"));
        Assert.False(sanitized.Values.ContainsKey("fd_detail000"));
    }

    [Fact]
    public void Progress_counts_only_visible_effectively_required_fields()
    {
        var definition = ConditionalDefinition();
        var engine = EngineTestFactory.Create();
        var hiddenAnswers = Answers(new Dictionary<string, object> { ["fd_toggle000"] = false });
        var visibleAnswers = Answers(new Dictionary<string, object> { ["fd_toggle000"] = true, ["fd_detail000"] = "done" });

        var hiddenProgress = engine.CalculateProgress(definition, hiddenAnswers, engine.EvaluateVisibility(definition, hiddenAnswers));
        var visibleProgress = engine.CalculateProgress(definition, visibleAnswers, engine.EvaluateVisibility(definition, visibleAnswers));

        Assert.Equal((1, 1), (hiddenProgress.Required, hiddenProgress.Answered));
        Assert.Equal((2, 2), (visibleProgress.Required, visibleProgress.Answered));
    }

    [Fact]
    public void Repeater_minimum_applies_only_on_submit()
    {
        var definition = RepeaterDefinition();
        var answers = Answers(new Dictionary<string, object> { ["sc_repeat000"] = new { rows = Array.Empty<object>() } });
        var engine = EngineTestFactory.Create();

        Assert.DoesNotContain(engine.ValidateAnswers(definition, answers, AnswersValidationMode.Draft).Issues, issue => issue.Code == "answer.rows_below_min");
        Assert.Contains(engine.ValidateAnswers(definition, answers, AnswersValidationMode.Submit).Issues, issue => issue.Code == "answer.rows_below_min");
    }

    [Fact]
    public void Repeater_rejects_duplicate_row_ids()
    {
        var definition = RepeaterDefinition();
        var rows = new[]
        {
            new { rowId = "rw_abcdefghij", values = new Dictionary<string, object> { ["fd_rowname00"] = "A" } },
            new { rowId = "rw_abcdefghij", values = new Dictionary<string, object> { ["fd_rowname00"] = "B" } },
        };
        var answers = Answers(new Dictionary<string, object> { ["sc_repeat000"] = new { rows } });

        var report = EngineTestFactory.Create().ValidateAnswers(definition, answers, AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code == "answer.row_id_duplicate");
    }

    [Fact]
    public void Rows_condition_controls_later_section_visibility()
    {
        var repeat = RepeaterDefinition();
        var later = new FormSection
        {
            Id = "sc_later0000",
            Title = "Later",
            VisibleWhen = new Condition
            {
                Rows = "sc_repeat000",
                Op = "countGte",
                Value = JsonSerializer.SerializeToElement(1),
            },
            Components = [new FormComponent { Id = "fd_later0000", Type = "text", Label = "Later field", Props = JsonSerializer.SerializeToElement(new { }) }],
        };
        var definition = EngineTestFactory.WithPages(
            repeat,
            [new FormPage { Id = "pg_abcdefgh", Title = "Page", Sections = [repeat.Pages[0].Sections[0], later] }]
        );
        var rows = new[] { new { rowId = "rw_abcdefghij", values = new Dictionary<string, object> { ["fd_rowname00"] = "A" } } };
        var answers = Answers(new Dictionary<string, object> { ["sc_repeat000"] = new { rows } });

        var visibility = EngineTestFactory.Create().EvaluateVisibility(definition, answers);

        Assert.True(visibility.IsVisible("sc_later0000"));
        Assert.True(visibility.IsVisible("fd_later0000"));
    }

    [Fact]
    public void Canonical_hash_is_independent_of_props_key_order()
    {
        var first = EngineTestFactory.Definition("text", JsonSerializer.Deserialize<JsonElement>("{\"maxLength\":200,\"minLength\":1}"));
        var second = EngineTestFactory.Definition("text", JsonSerializer.Deserialize<JsonElement>("{\"minLength\":1,\"maxLength\":200}"));
        var engine = EngineTestFactory.Create();

        Assert.Equal(engine.Canonicalize(first).Sha256, engine.Canonicalize(second).Sha256);
        Assert.Equal(engine.Canonicalize(first).Json, engine.Canonicalize(second).Json);
    }

    private static FormDefinition ConditionalDefinition()
    {
        var toggle = new FormComponent { Id = "fd_toggle000", Type = "yesNo", Label = "Continue?", Required = true, Props = JsonSerializer.SerializeToElement(new { }) };
        var detail = new FormComponent
        {
            Id = "fd_detail000",
            Type = "text",
            Label = "Details",
            Required = true,
            VisibleWhen = new Condition { Field = toggle.Id, Op = "isTrue" },
            Props = JsonSerializer.SerializeToElement(new { }),
        };
        return EngineTestFactory.WithComponents(toggle, detail);
    }

    private static FormDefinition RepeaterDefinition() =>
        new()
        {
            SchemaVersion = 1,
            Title = "Dependents",
            Purpose = FormPurpose.ClientIntake,
            Presentation = FormPresentation.Wizard,
            Pages =
            [
                new FormPage
                {
                    Id = "pg_abcdefgh",
                    Title = "Page",
                    Sections =
                    [
                        new FormSection
                        {
                            Id = "sc_repeat000",
                            Title = "Rows",
                            Repeat = new FormRepeat { MinRows = 1, MaxRows = 3, AddLabel = "Add row", RowLabel = "Row" },
                            Components = [new FormComponent { Id = "fd_rowname00", Type = "text", Label = "Name", Required = true, Props = JsonSerializer.SerializeToElement(new { }) }],
                        },
                    ],
                },
            ],
        };

    private static FormAnswers Answers(IReadOnlyDictionary<string, object> values) =>
        new()
        {
            SchemaVersion = 1,
            Values = values.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value), StringComparer.Ordinal),
        };
}
