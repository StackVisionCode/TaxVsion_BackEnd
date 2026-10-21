using System.Text.Json;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Validation;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class AnswersValidatorTests
{
    [Theory]
    [InlineData("text", "hello", null)]
    [InlineData("textarea", "line one\nline two", null)]
    [InlineData("number", "12", null)]
    [InlineData("currency", "12.50", null)]
    [InlineData("date", "2026-01-31", null)]
    [InlineData("email", "person@example.com", null)]
    [InlineData("phone", "+18095551234", null)]
    [InlineData("yesNo", true, null)]
    [InlineData("checkbox", true, null)]
    public void Accepts_valid_scalar_answers(string type, object value, string? _)
    {
        var definition = EngineTestFactory.Definition(type, PropsFor(type));
        var report = EngineTestFactory.Create().ValidateAnswers(definition, EngineTestFactory.Answers(value), AnswersValidationMode.Submit);

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Issues.Select(issue => issue.Code)));
    }

    [Theory]
    [InlineData("text", 12)]
    [InlineData("number", 12)]
    [InlineData("email", true)]
    [InlineData("phone", false)]
    [InlineData("yesNo", "yes")]
    [InlineData("checkbox", "true")]
    public void Rejects_type_mismatches(string type, object value)
    {
        var definition = EngineTestFactory.Definition(type, PropsFor(type));
        var report = EngineTestFactory.Create().ValidateAnswers(definition, EngineTestFactory.Answers(value), AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code is "answer.type_mismatch" or "answer.invalid_email" or "answer.invalid_phone");
    }

    [Theory]
    [InlineData("2026-02-30", "answer.invalid_date")]
    [InlineData("person example.com", "answer.invalid_email")]
    [InlineData("5551234567", "answer.invalid_phone")]
    public void Rejects_invalid_formatted_values(string value, string code)
    {
        var type = code switch
        {
            "answer.invalid_date" => "date",
            "answer.invalid_email" => "email",
            _ => "phone",
        };
        var report = EngineTestFactory.Create().ValidateAnswers(EngineTestFactory.Definition(type), EngineTestFactory.Answers(value), AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code == code);
    }

    [Theory]
    [InlineData("123-45-6789", "answer.looks_like_tax_identifier")]
    [InlineData("4111 1111 1111 1111", "answer.looks_like_card_number")]
    public void Rejects_sensitive_patterns_in_free_text(string value, string code)
    {
        var report = EngineTestFactory.Create().ValidateAnswers(EngineTestFactory.Definition(), EngineTestFactory.Answers(value), AnswersValidationMode.Draft);

        Assert.Contains(report.Issues, issue => issue.Code == code);
    }

    [Theory]
    [InlineData("12.34", 0, "answer.too_many_decimals")]
    [InlineData("101", 2, "answer.out_of_range")]
    public void Enforces_numeric_constraints(string value, int decimals, string code)
    {
        var definition = EngineTestFactory.Definition("number", new { min = 0, max = 100, decimals });
        var report = EngineTestFactory.Create().ValidateAnswers(definition, EngineTestFactory.Answers(value), AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code == code);
    }

    [Fact]
    public void Required_checkbox_must_be_true()
    {
        var definition = EngineTestFactory.Definition("checkbox", new { text = RichText("I agree") }, required: true);
        var report = EngineTestFactory.Create().ValidateAnswers(definition, EngineTestFactory.Answers(false), AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code == "answer.must_be_checked");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("third")]
    public void Choice_requires_a_defined_option(string value)
    {
        var definition = EngineTestFactory.Definition("select", ChoiceProps());
        var report = EngineTestFactory.Create().ValidateAnswers(definition, EngineTestFactory.Answers(value), AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code == "answer.invalid_option");
    }

    [Fact]
    public void Checkbox_group_rejects_duplicates_and_count()
    {
        var definition = EngineTestFactory.Definition("checkboxGroup", new { options = Options(), minSelected = 1, maxSelected = 1 });
        var report = EngineTestFactory.Create().ValidateAnswers(definition, EngineTestFactory.Answers(new[] { "first", "first" }), AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code == "answer.invalid_option");
        Assert.Contains(report.Issues, issue => issue.Code == "answer.selection_count");
    }

    [Fact]
    public void File_answer_rejects_duplicates_and_excess()
    {
        var fileId = Guid.NewGuid();
        var definition = EngineTestFactory.Definition("file", new { maxFiles = 1 });
        var report = EngineTestFactory.Create().ValidateAnswers(
            definition,
            EngineTestFactory.Answers(new[] { new { fileId }, new { fileId } }),
            AnswersValidationMode.Submit
        );

        Assert.Contains(report.Issues, issue => issue.Code == "answer.too_many_files");
        Assert.Contains(report.Issues, issue => issue.Code == "answer.type_mismatch");
    }

    [Fact]
    public void Partial_address_is_allowed_in_draft_but_not_submit()
    {
        var definition = EngineTestFactory.Definition("address", new { country = "US" });
        var answers = EngineTestFactory.Answers(new { line1 = "10 Main St", country = "US" });
        var engine = EngineTestFactory.Create();

        Assert.True(engine.ValidateAnswers(definition, answers, AnswersValidationMode.Draft).IsValid);
        Assert.Contains(engine.ValidateAnswers(definition, answers, AnswersValidationMode.Submit).Issues, issue => issue.Code == "answer.required");
    }

    [Theory]
    [InlineData("XX", "12345", "answer.invalid_state")]
    [InlineData("NY", "123", "answer.invalid_postal_code")]
    public void Address_validates_state_and_postal_code(string state, string postalCode, string code)
    {
        var definition = EngineTestFactory.Definition("address", new { country = "US" });
        var answers = EngineTestFactory.Answers(new { line1 = "10 Main St", city = "Albany", state, postalCode, country = "US" });
        var report = EngineTestFactory.Create().ValidateAnswers(definition, answers, AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code == code);
    }

    [Theory]
    [InlineData("not-a-guid", "1234", "ssn")]
    [InlineData("00000000-0000-0000-0000-000000000001", "123", "ssn")]
    [InlineData("00000000-0000-0000-0000-000000000001", "1234", "ein")]
    public void Tax_id_accepts_only_server_reference_shape(string sensitiveRef, string last4, string kind)
    {
        var definition = EngineTestFactory.Definition("taxId", new { kinds = new[] { "ssn", "itin" } });
        var report = EngineTestFactory.Create().ValidateAnswers(definition, EngineTestFactory.Answers(new { sensitiveRef, last4, kind }), AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code == "answer.sensitive_invalid");
    }

    [Fact]
    public void Unknown_answer_key_is_rejected()
    {
        var answers = new FormAnswers
        {
            SchemaVersion = 1,
            Values = new Dictionary<string, JsonElement> { ["fd_unknown00"] = JsonSerializer.SerializeToElement("value") },
        };
        var report = EngineTestFactory.Create().ValidateAnswers(EngineTestFactory.Definition(), answers, AnswersValidationMode.Draft);

        Assert.Contains(report.Issues, issue => issue.Code == "answer.unknown_component");
    }

    [Fact]
    public void Address_rejects_unknown_nested_properties()
    {
        var definition = EngineTestFactory.Definition("address", new { country = "US" });
        var answers = EngineTestFactory.Answers(new
        {
            line1 = "10 Main St",
            city = "Albany",
            state = "NY",
            postalCode = "12207",
            country = "US",
            isAdmin = true,
        });

        var report = EngineTestFactory.Create().ValidateAnswers(definition, answers, AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code == "answer.type_mismatch");
    }

    [Fact]
    public void Tax_id_rejects_unknown_nested_properties()
    {
        var definition = EngineTestFactory.Definition("taxId", new { kinds = new[] { "ssn" } });
        var answers = EngineTestFactory.Answers(new
        {
            sensitiveRef = Guid.NewGuid(),
            last4 = "1234",
            kind = "ssn",
            plaintext = "123456789",
        });

        var report = EngineTestFactory.Create().ValidateAnswers(definition, answers, AnswersValidationMode.Submit);

        Assert.Contains(report.Issues, issue => issue.Code == "answer.sensitive_invalid");
    }

    private static object PropsFor(string type) => type switch
    {
        "text" => new { maxLength = 200 },
        "textarea" => new { maxLength = 2_000, rows = 4 },
        "number" => new { decimals = 0 },
        "currency" => new { currency = "USD" },
        _ => new { },
    };

    private static object ChoiceProps() => new { options = Options() };
    private static object[] Options() => [new { value = "first", label = "First" }, new { value = "second", label = "Second" }];
    private static object RichText(string text) => new { type = "doc", blocks = new[] { new { type = "paragraph", children = new[] { new { type = "text", text } } } } };
}
