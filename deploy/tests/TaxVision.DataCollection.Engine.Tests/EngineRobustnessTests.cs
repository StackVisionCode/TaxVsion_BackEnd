using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Formats;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class EngineRobustnessTests
{
    [Fact]
    public void Draft_accepts_unanswered_required_fields_that_submit_rejects()
    {
        var required = new FormComponent
        {
            Id = "fd_required0",
            Type = "text",
            Label = "Name",
            Required = true,
            Props = Empty(),
        };
        var accept = new FormComponent
        {
            Id = "fd_accept000",
            Type = "checkbox",
            Label = "I agree",
            Required = true,
            Props = Empty(),
        };
        var definition = EngineTestFactory.WithComponents(required, accept);
        var answers = new FormAnswers { SchemaVersion = 1, Values = new Dictionary<string, JsonElement>() };
        var engine = EngineTestFactory.Create();

        Assert.Empty(engine.ValidateAnswers(definition, answers, AnswersValidationMode.Draft).Issues);
        Assert.Contains(
            engine.ValidateAnswers(definition, answers, AnswersValidationMode.Submit).Issues,
            i => i.Code == "answer.required"
        );
    }

    [Fact]
    public void Canonicalize_handles_conditions_and_components_without_value_or_props()
    {
        var toggle = new FormComponent
        {
            Id = "fd_toggle000",
            Type = "yesNo",
            Label = "Continue?",
        };
        var detail = new FormComponent
        {
            Id = "fd_detail000",
            Type = "text",
            Label = "Details",
            VisibleWhen = new Condition { Field = toggle.Id, Op = "isTrue" },
        };
        var engine = EngineTestFactory.Create();

        var canonical = engine.Canonicalize(EngineTestFactory.WithComponents(toggle, detail));

        Assert.DoesNotContain("\"value\"", canonical.Json);
        Assert.Equal(64, canonical.Sha256.Length);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"values\":null}")]
    [InlineData("{\"schemaVersion\":1}")]
    public void Answers_with_null_or_missing_values_are_rejected_as_invalid_json(string json)
    {
        var parsed = EngineTestFactory.Create().ParseAnswers(json);

        Assert.True(parsed.IsFailure);
    }

    [Theory]
    [InlineData("date", "{}", "{\"field\":\"fd_source000\",\"op\":\"gt\",\"value\":20260101}", "\"2026-10-20\"", false)]
    [InlineData(
        "checkboxGroup",
        Choices,
        "{\"field\":\"fd_source000\",\"op\":\"contains\",\"value\":\"first\"}",
        "[1,2]",
        false
    )]
    [InlineData(
        "checkboxGroup",
        Choices,
        "{\"field\":\"fd_source000\",\"op\":\"notContains\",\"value\":\"first\"}",
        "[1,2]",
        false
    )]
    [InlineData(
        "checkboxGroup",
        Choices,
        "{\"field\":\"fd_source000\",\"op\":\"notContains\",\"value\":\"first\"}",
        "[\"second\"]",
        true
    )]
    [InlineData("number", "{}", "{\"field\":\"fd_source000\",\"op\":\"equals\",\"value\":1000}", "\"1,000\"", false)]
    [InlineData("number", "{}", "{\"field\":\"fd_source000\",\"op\":\"notEquals\",\"value\":1000}", "\"1,000\"", false)]
    [InlineData(
        "number",
        "{\"decimals\":1}",
        "{\"field\":\"fd_source000\",\"op\":\"equals\",\"value\":1000}",
        "\"1000.0\"",
        true
    )]
    [InlineData("number", "{}", "{\"field\":\"fd_source000\",\"op\":\"gt\",\"value\":\"5\"}", "\"10\"", false)]
    [InlineData("select", Choices, "{\"field\":\"fd_source000\",\"op\":\"notIn\",\"value\":[1]}", "\"first\"", false)]
    [InlineData("text", "{}", "{}", "\"value\"", false)]
    public void Conditions_with_unexpected_kinds_evaluate_to_false_without_throwing(
        string type,
        string props,
        string condition,
        string answer,
        bool visible
    )
    {
        var engine = EngineTestFactory.Create();
        var definition = engine
            .ParseDefinition(
                $$$"""
                {"schemaVersion":1,"title":"Form","purpose":"clientIntake","presentation":"wizard","pages":[{"id":"pg_abcdefgh","title":"Page","sections":[{"id":"sc_abcdefgh","components":[
                  {"id":"fd_source000","type":"{{{type}}}","label":"Source","props":{{{props}}}},
                  {"id":"fd_detail000","type":"text","label":"Detail","visibleWhen":{{{condition}}}}]}]}]}
                """
            )
            .Value;
        var answers = engine.ParseAnswers($$$"""{"schemaVersion":1,"values":{"fd_source000":{{{answer}}}}}""").Value;

        Assert.Equal(visible, engine.EvaluateVisibility(definition, answers).IsVisible("fd_detail000"));
    }

    [Fact]
    public void Rows_condition_with_a_non_numeric_value_is_false()
    {
        var engine = EngineTestFactory.Create();
        var definition = engine
            .ParseDefinition(
                RepeaterJson(
                    maxRows: 3,
                    laterCondition: "{\"rows\":\"sc_repeat000\",\"op\":\"countGte\",\"value\":\"1\"}"
                )
            )
            .Value;
        var answers = engine.ParseAnswers(RowsJson(1)).Value;

        Assert.False(engine.EvaluateVisibility(definition, answers).IsVisible("fd_later0000"));
    }

    [Fact]
    public void Oversized_repeater_payload_is_capped_before_evaluation()
    {
        var engine = EngineTestFactory.Create();
        var definition = engine
            .ParseDefinition(
                RepeaterJson(maxRows: 3, laterCondition: "{\"rows\":\"sc_repeat000\",\"op\":\"countGte\",\"value\":1}")
            )
            .Value;
        // ~25k filas mínimas caben en 1 MB; antes la visibilidad copiaba todo el mapa por componente y fila.
        var parsed = engine.ParseAnswers(RowsJson(25_000));
        Assert.True(parsed.IsSuccess);
        var answers = parsed.Value;
        var watch = Stopwatch.StartNew();

        var visibility = engine.EvaluateVisibility(definition, answers);
        var draft = engine.ValidateAnswers(definition, answers, AnswersValidationMode.Draft);
        var progress = engine.CalculateProgress(definition, answers, visibility);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Took {watch.Elapsed}.");
        Assert.True(visibility.IsVisible("fd_rowflag00", "rw_0000000002"));
        Assert.False(visibility.IsVisible("fd_rowflag00", "rw_0000000003"));
        Assert.True(visibility.IsVisible("fd_later0000"));
        Assert.Equal(new[] { "answer.rows_above_max" }, draft.Issues.Select(issue => issue.Code));
        Assert.Equal(3, progress.Required);
    }

    [Fact]
    public void Hidden_answers_are_validated_in_draft_but_ignored_on_submit()
    {
        var toggle = new FormComponent
        {
            Id = "fd_toggle000",
            Type = "yesNo",
            Label = "Continue?",
            Props = Empty(),
        };
        var detail = new FormComponent
        {
            Id = "fd_detail000",
            Type = "text",
            Label = "Details",
            Required = true,
            VisibleWhen = new Condition { Field = toggle.Id, Op = "isTrue" },
            Props = JsonSerializer.SerializeToElement(new { maxLength = 5 }),
        };
        var definition = EngineTestFactory.WithComponents(toggle, detail);
        var answers = new FormAnswers
        {
            SchemaVersion = 1,
            Values = new Dictionary<string, JsonElement>
            {
                ["fd_toggle000"] = JsonSerializer.SerializeToElement(false),
                ["fd_detail000"] = JsonSerializer.SerializeToElement("123-45-6789"),
            },
        };
        var engine = EngineTestFactory.Create();

        var draft = engine
            .ValidateAnswers(definition, answers, AnswersValidationMode.Draft)
            .Issues.Select(issue => issue.Code)
            .ToArray();

        Assert.Contains("answer.too_long", draft);
        Assert.Contains("answer.looks_like_tax_identifier", draft);
        Assert.DoesNotContain("answer.required", draft);
        Assert.Empty(engine.ValidateAnswers(definition, answers, AnswersValidationMode.Submit).Issues);
    }

    [Fact]
    public void Dates_parse_with_the_invariant_calendar_whatever_the_thread_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        try
        {
            // 2024 es bisiesto en el calendario gregoriano, no en el budista tailandés.
            var definition = EngineTestFactory.Definition("date", new { min = "2024-01-01" });
            var report = EngineTestFactory
                .Create()
                .ValidateAnswers(definition, EngineTestFactory.Answers("2024-02-29"), AnswersValidationMode.Submit);

            Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Issues.Select(issue => issue.Code)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("+18095551234\n")]
    [InlineData("+1809555١٢٣٤")]
    public void Phone_format_rejects_trailing_newline_and_unicode_digits(string value)
    {
        Assert.False(PhoneFormat.IsValid(value));
    }

    [Theory]
    [InlineData("12207\n")]
    [InlineData("١٢٢٠٧")]
    public void Postal_code_format_rejects_trailing_newline_and_unicode_digits(string value)
    {
        Assert.False(PostalCodeFormat.IsValid(value));
    }

    private const string Choices =
        "{\"options\":[{\"value\":\"first\",\"label\":\"First\"},{\"value\":\"second\",\"label\":\"Second\"}]}";

    private static string RepeaterJson(int maxRows, string laterCondition) =>
        $$$"""
            {"schemaVersion":1,"title":"Form","purpose":"clientIntake","presentation":"wizard","pages":[{"id":"pg_abcdefgh","title":"Page","sections":[
              {"id":"sc_repeat000","title":"Rows","repeat":{"minRows":0,"maxRows":{{{maxRows}}},"addLabel":"Add","rowLabel":"Row"},"components":[
                {"id":"fd_rowname00","type":"text","label":"Name"},
                {"id":"fd_rowflag00","type":"yesNo","label":"Flag","required":true,"visibleWhen":{"field":"fd_rowname00","op":"isNotAnswered"}}]},
              {"id":"sc_later0000","title":"Later","visibleWhen":{{{laterCondition}}},"components":[{"id":"fd_later0000","type":"text","label":"Later"}]}]}]}
            """;

    private static string RowsJson(int count) =>
        "{\"schemaVersion\":1,\"values\":{\"sc_repeat000\":{\"rows\":["
        + string.Join(
            ',',
            Enumerable.Range(0, count).Select(index => $"{{\"rowId\":\"rw_{index:D10}\",\"values\":{{}}}}")
        )
        + "]}}}";

    private static JsonElement Empty() => JsonSerializer.SerializeToElement(new { });
}
