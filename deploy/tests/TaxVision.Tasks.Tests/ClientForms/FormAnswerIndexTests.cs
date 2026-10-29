using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Reporting;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormAnswerIndexTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-00000000c012");
    private static readonly Guid RequestId = Guid.Parse("20000000-0000-0000-0000-00000000c012");
    private static readonly Guid TemplateId = Guid.Parse("30000000-0000-0000-0000-00000000c012");
    private static readonly Guid CustomerId = Guid.Parse("40000000-0000-0000-0000-00000000c012");
    private static readonly DateTime CompletedAt = new(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc);

    // Definición con un componente reportable de cada tipo + un repeater + taxId/file reportables (que
    // deben excluirse) + un texto no reportable.
    private const string Definition = """
        {"schemaVersion":1,"title":"Intake","purpose":"clientIntake","presentation":"singlePage","settings":{"showProgress":true},
        "pages":[{"id":"pg_flat0001","sections":[{"id":"sc_flat0001","components":[
          {"id":"fd_crypto001","type":"yesNo","key":"crypto","label":"Owned crypto","dataClass":"general","reportable":true,"props":{}},
          {"id":"fd_filing001","type":"select","key":"filing_status","label":"Filing status","dataClass":"general","reportable":true,"props":{"options":[]}},
          {"id":"fd_income001","type":"number","key":"income","label":"Income","dataClass":"financial","reportable":true,"props":{}},
          {"id":"fd_refund001","type":"currency","key":"refund","label":"Refund","dataClass":"financial","reportable":true,"props":{}},
          {"id":"fd_signed001","type":"date","key":"signed_on","label":"Signed on","dataClass":"general","reportable":true,"props":{}},
          {"id":"fd_note00001","type":"text","key":"note_key","label":"Note","dataClass":"general","reportable":true,"props":{}},
          {"id":"fd_docs00001","type":"checkboxGroup","key":"docs","label":"Documents","dataClass":"general","reportable":true,"props":{"options":[]}},
          {"id":"fd_plain0001","type":"text","key":"plain_key","label":"Plain","dataClass":"general","reportable":false,"props":{}},
          {"id":"fd_taxid0001","type":"taxId","key":"ssn_key","label":"SSN","dataClass":"restricted","reportable":true,"props":{"kinds":["ssn"]}},
          {"id":"fd_file00001","type":"file","key":"file_key","label":"File","dataClass":"personal","reportable":true,"props":{"allowedTypes":["application/pdf"],"maxFiles":1,"maxFileBytes":100}}
        ]}]},
        {"id":"pg_rep00001","sections":[{"id":"sc_rep00001","repeat":{"minRows":0,"maxRows":5,"addLabel":"Add","rowLabel":"Dependent"},"components":[
          {"id":"fd_depname01","type":"text","key":"dependent_name","label":"Dependent","dataClass":"general","reportable":true,"props":{}}
        ]}]}],
        "mappings":[],"actions":[]}
        """;

    private const string Answers = """
        {"schemaVersion":1,"values":{
          "fd_crypto001":true,
          "fd_filing001":"mfj",
          "fd_income001":"50000.50",
          "fd_refund001":"1200",
          "fd_signed001":"2026-04-15",
          "fd_note00001":"  Hello world  ",
          "fd_docs00001":["w2","1099"],
          "fd_plain0001":"not reported",
          "fd_taxid0001":{"kind":"ssn"},
          "fd_file00001":["file-1"],
          "sc_rep00001":{"rows":[
            {"rowId":"rw_00000001","values":{"fd_depname01":"Alice"}},
            {"rowId":"rw_00000002","values":{"fd_depname01":"Bob"}}
          ]}
        }}
        """;

    private static IReadOnlyList<FormAnswerIndexEntry> Build()
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var definition = engine.ParseDefinition(Definition).Value;
        var answers = engine.ParseAnswers(Answers).Value;
        var context = new FormAnswerIndexContext(TenantId, RequestId, TemplateId, 3, CustomerId, null, 1, CompletedAt);
        return FormAnswerIndexBuilder.Build(definition, answers, context);
    }

    [Fact]
    public void Indexes_every_reportable_type_and_normalizes_each_value()
    {
        var byKey = Build().ToLookup(entry => entry.Key);

        Assert.Equal("Yes", Assert.Single(byKey["crypto"]).ValueText);
        Assert.Equal("mfj", Assert.Single(byKey["filing_status"]).ValueText);

        var income = Assert.Single(byKey["income"]);
        Assert.Equal("50000.50", income.ValueText);
        Assert.Equal(50000.50m, income.ValueNumber);

        var refund = Assert.Single(byKey["refund"]);
        Assert.Equal(1200m, refund.ValueNumber);

        var signed = Assert.Single(byKey["signed_on"]);
        Assert.Equal(new DateOnly(2026, 4, 15), signed.ValueDate);
        Assert.Equal("2026-04-15", signed.ValueText);

        Assert.Equal("Hello world", Assert.Single(byKey["note_key"]).ValueText);
        Assert.Equal(["w2", "1099"], byKey["docs"].Select(entry => entry.ValueText).ToArray());
    }

    [Fact]
    public void Carries_the_request_metadata_on_each_row()
    {
        var entry = Build().First(item => item.Key == "crypto");

        Assert.Equal(TenantId, entry.TenantId);
        Assert.Equal(RequestId, entry.FormRequestId);
        Assert.Equal(TemplateId, entry.FormTemplateId);
        Assert.Equal(CustomerId, entry.CustomerId);
        Assert.Null(entry.ParticipantId);
        Assert.Equal(1, entry.RevisionNumber);
        Assert.Equal(3, entry.VersionNumber);
        Assert.Equal(CompletedAt, entry.CompletedAtUtc);
        Assert.Null(entry.RowIndex);
    }

    [Fact]
    public void Repeater_rows_are_indexed_with_a_one_based_row_index()
    {
        var rows = Build().Where(entry => entry.Key == "dependent_name").OrderBy(entry => entry.RowIndex).ToArray();

        Assert.Equal(["Alice", "Bob"], rows.Select(entry => entry.ValueText).ToArray());
        Assert.Equal([1, 2], rows.Select(entry => entry.RowIndex).ToArray());
    }

    [Fact]
    public void Never_indexes_tax_id_file_or_non_reportable_components()
    {
        var keys = Build().Select(entry => entry.Key).ToHashSet();

        Assert.DoesNotContain("ssn_key", keys);
        Assert.DoesNotContain("file_key", keys);
        Assert.DoesNotContain("plain_key", keys);
    }

    [Fact]
    public void A_false_yes_no_indexes_No_and_an_unparseable_number_is_skipped()
    {
        Assert.Equal([new FormReportCell("No", null, null)], Normalize("yesNo", "false"));
        Assert.Empty(Normalize("number", "\"abc\""));
        Assert.Empty(Normalize("text", "\"\""));
    }

    private static IReadOnlyList<FormReportCell> Normalize(string type, string json) =>
        FormReportValueNormalizer.Normalize(type, System.Text.Json.JsonDocument.Parse(json).RootElement);

    [Theory]
    [InlineData("=cmd()", "'=cmd()")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tTabbed", "'\tTabbed")]
    [InlineData("Safe value", "Safe value")]
    [InlineData("", "")]
    public void Csv_guard_prefixes_formula_injection(string input, string expected) =>
        Assert.Equal(expected, FormReportCsv.Guard(input));
}
