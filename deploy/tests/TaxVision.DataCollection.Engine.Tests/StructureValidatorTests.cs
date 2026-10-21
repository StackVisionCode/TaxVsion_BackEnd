using System.Text.Json;
using TaxVision.DataCollection.Engine.Conditions;
using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Tests;

public sealed class StructureValidatorTests
{
    [Fact]
    public void Minimal_text_definition_is_publishable()
    {
        var report = EngineTestFactory.Create().ValidateForPublish(EngineTestFactory.Definition());

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Issues.Select(issue => issue.Code)));
    }

    [Theory]
    [InlineData("unknown", "component.unknown_type")]
    [InlineData("text", "component.label_required")]
    public void Rejects_invalid_component_contract(string type, string expectedCode)
    {
        var definition = EngineTestFactory.Definition(type, label: type == "text" ? null : "Your answer");

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == expectedCode);
    }

    [Theory]
    [InlineData("pg_bad", "id.invalid_format")]
    [InlineData("pg_abcdefgh", "id.duplicate")]
    public void Validates_ids(string secondPageId, string expectedCode)
    {
        var definition = EngineTestFactory.Definition();
        definition = EngineTestFactory.WithPages(
            definition,
            [definition.Pages[0], new FormPage { Id = secondPageId, Title = "Second", Sections = [new FormSection { Id = "sc_ijklmnop", Components = [new FormComponent { Id = "fd_ijklmnop", Type = "text", Label = "Other", Props = JsonSerializer.SerializeToElement(new { }) }] }] }]
        );

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public void Single_page_presentation_rejects_two_pages()
    {
        var source = EngineTestFactory.Definition();
        var definition = new FormDefinition
        {
            SchemaVersion = 1,
            Title = source.Title,
            Purpose = source.Purpose,
            Presentation = FormPresentation.SinglePage,
            Pages = [source.Pages[0], new FormPage { Id = "pg_ijklmnop", Sections = [new FormSection { Id = "sc_ijklmnop", Components = [new FormComponent { Id = "fd_ijklmnop", Type = "text", Label = "Other", Props = JsonSerializer.SerializeToElement(new { }) }] }] }],
        };

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == "page.single_page_violation");
    }

    [Fact]
    public void Required_and_required_when_conflict()
    {
        var definition = EngineTestFactory.Definition(
            required: true,
            requiredWhen: new Condition { Field = "fd_abcdefgh", Op = "isAnswered" }
        );

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == "required.conflict");
    }

    [Theory]
    [InlineData(DataClass.General)]
    [InlineData(DataClass.Financial)]
    public void Text_cannot_lower_its_personal_data_class(DataClass declared)
    {
        var report = EngineTestFactory.Create().ValidateForPublish(EngineTestFactory.Definition(dataClass: declared));

        Assert.Contains(report.Issues, issue => issue.Code == "dataclass.below_minimum");
    }

    [Theory]
    [InlineData("Enter SSN", "component.restricted_requires_taxid")]
    [InlineData("Enter routing number", "component.unsupported_restricted_data")]
    [InlineData("Enter card number", "component.unsupported_restricted_data")]
    [InlineData("Enter password", "component.unsupported_restricted_data")]
    public void Rejects_restricted_labels_on_free_text(string label, string expectedCode)
    {
        var definition = EngineTestFactory.Definition(label: label);

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == expectedCode);
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("exe")]
    public void File_component_rejects_unsupported_types(string extension)
    {
        var definition = EngineTestFactory.Definition("file", new { allowedTypes = new[] { "pdf", extension } });

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == "file.type_not_allowed");
    }

    [Fact]
    public void Forward_condition_is_rejected()
    {
        var first = new FormComponent
        {
            Id = "fd_abcdefgh",
            Type = "text",
            Label = "First",
            Props = JsonSerializer.SerializeToElement(new { }),
            VisibleWhen = new Condition { Field = "fd_ijklmnop", Op = "isTrue" },
        };
        var second = new FormComponent { Id = "fd_ijklmnop", Type = "yesNo", Label = "Later", Props = JsonSerializer.SerializeToElement(new { }) };
        var definition = EngineTestFactory.WithComponents(first, second);

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == "condition.forward_reference");
    }

    [Fact]
    public void Signature_step_requires_action()
    {
        var definition = EngineTestFactory.Definition("signatureStep", new { documentSource = "submissionSummary", verification = "emailOtp" });

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == "signature.action_missing");
    }

    [Fact]
    public void Rejects_wrong_property_json_type()
    {
        var definition = EngineTestFactory.Definition("text", new { maxLength = "200" });

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == "props.out_of_range" && issue.Path.EndsWith("/maxLength", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_condition_properties_from_another_shape()
    {
        var first = new FormComponent
        {
            Id = "fd_abcdefgh",
            Type = "yesNo",
            Label = "First",
            Props = JsonSerializer.SerializeToElement(new { }),
        };
        var second = new FormComponent
        {
            Id = "fd_ijklmnop",
            Type = "text",
            Label = "Second",
            Props = JsonSerializer.SerializeToElement(new { }),
            VisibleWhen = new Condition
            {
                All = [new Condition { Field = "fd_abcdefgh", Op = "isTrue" }],
                Op = "isAnswered",
            },
        };

        var report = EngineTestFactory.Create().ValidateForPublish(EngineTestFactory.WithComponents(first, second));

        Assert.Contains(report.Issues, issue => issue.Code == "condition.value_invalid");
    }

    [Fact]
    public void Rejects_unknown_create_customer_config()
    {
        var source = EngineTestFactory.Definition();
        var definition = new FormDefinition
        {
            SchemaVersion = source.SchemaVersion,
            Title = source.Title,
            Purpose = source.Purpose,
            Presentation = source.Presentation,
            Pages = source.Pages,
            Actions =
            [
                new FormAction
                {
                    Key = "create_client",
                    Type = FormActionType.CreateCustomer,
                    Trigger = FormActionTrigger.OnSubmit,
                    Config = JsonSerializer.SerializeToElement(new { endpoint = "https://example.com" }),
                },
            ],
        };

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == "props.unknown" && issue.Path.EndsWith("/endpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void Validates_action_condition_against_definition()
    {
        var source = EngineTestFactory.Definition();
        var definition = new FormDefinition
        {
            SchemaVersion = source.SchemaVersion,
            Title = source.Title,
            Purpose = source.Purpose,
            Presentation = source.Presentation,
            Pages = source.Pages,
            Actions =
            [
                new FormAction
                {
                    Key = "summary",
                    Type = FormActionType.GenerateSummary,
                    Trigger = FormActionTrigger.OnComplete,
                    Condition = new Condition { Field = "fd_unknown00", Op = "isAnswered" },
                },
            ],
        };

        var report = EngineTestFactory.Create().ValidateForPublish(definition);

        Assert.Contains(report.Issues, issue => issue.Code == "condition.unknown_field");
    }
}
