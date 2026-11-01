using BuildingBlocks.Common;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Reporting;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Tests.ClientForms;

// El índice guarda el valor estable de la opción (option1); el reporte muestra lo que vio el cliente.
public sealed class FormReportLabelsTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-00000000d101");
    private static readonly Guid TemplateId = Guid.Parse("30000000-0000-0000-0000-00000000d101");
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static string Definition(string incomeOptions, string yesLabel) =>
        $$$"""
            {"schemaVersion":1,"title":"Income","purpose":"clientIntake","presentation":"singlePage","settings":{"showProgress":true},
            "pages":[{"id":"pg_income01","sections":[{"id":"sc_income01","components":[
              {"id":"fd_income001","type":"radio","key":"monthly_income","label":"Cuanto ganas mensualmente?","dataClass":"general","reportable":true,"props":{"options":[{{{incomeOptions}}}]}},
              {"id":"fd_crypto001","type":"yesNo","key":"crypto","label":"Crypto","dataClass":"general","reportable":true,"props":{"yesLabel":"{{{yesLabel}}}","noLabel":"No tengo"}},
              {"id":"fd_note00001","type":"text","key":"note_key","label":"Note","dataClass":"general","reportable":true,"props":{}}
            ]}]}],"mappings":[],"actions":[]}
            """;

    private static readonly string V1 = Definition(
        """{"value":"option1","label":"> 25K"},{"value":"option2","label":"< 25K"}""",
        "Sí"
    );

    // v2 renombra option1 y quita option2.
    private static readonly string V2 = Definition("""{"value":"option1","label":"More than 25K"}""", "Sí tengo");

    private static FormReportLabels Labels()
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        return FormReportLabels.From([(1, engine.ParseDefinition(V1).Value), (2, engine.ParseDefinition(V2).Value)]);
    }

    [Fact]
    public void Each_row_shows_the_label_of_the_version_the_client_answered()
    {
        var labels = Labels();

        Assert.Equal("> 25K", labels.Of(1, "monthly_income", "option1"));
        Assert.Equal("More than 25K", labels.Of(2, "monthly_income", "option1"));
    }

    [Fact]
    public void The_summary_uses_the_newest_label_and_falls_back_to_older_versions()
    {
        var labels = Labels();

        Assert.Equal("More than 25K", labels.Latest("monthly_income", "option1"));
        Assert.Equal("< 25K", labels.Latest("monthly_income", "option2"));
        Assert.Equal("< 25K", labels.Of(2, "monthly_income", "option2"));
    }

    [Fact]
    public void Yes_no_uses_the_author_labels_and_free_text_stays_as_answered()
    {
        var labels = Labels();

        Assert.Equal("Sí", labels.Of(1, "crypto", "Yes"));
        Assert.Equal("No tengo", labels.Of(1, "crypto", "No"));
        Assert.Equal("Hello", labels.Of(1, "note_key", "Hello"));
        Assert.Equal("option9", labels.Of(1, "monthly_income", "option9"));
    }

    [Fact]
    public void Reportable_keys_carry_their_options_for_the_filter()
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();

        var keys = FormReportSchema.ReportableKeys(engine.ParseDefinition(V1).Value).ToDictionary(key => key.Key);

        Assert.Equal(
            [new ComponentOption("option1", "> 25K"), new ComponentOption("option2", "< 25K")],
            keys["monthly_income"].Options
        );
        Assert.Equal([new ComponentOption("Yes", "Sí"), new ComponentOption("No", "No tengo")], keys["crypto"].Options);
        Assert.Empty(keys["note_key"].Options);
    }

    [Fact]
    public async Task The_answers_report_returns_the_label_next_to_the_stored_value()
    {
        var engine = FormTemplateHandlerTestDoubles.Engine();
        var versions = new MemoryFormTemplateVersionRepository(Version(engine, 1, V1), Version(engine, 2, V2));
        var read = new FixedRows(Row(1, "option1"), Row(2, "option1"));

        var page = await FormReportAnswersHandler.Handle(
            new FormReportAnswersQuery(
                TenantId,
                Guid.NewGuid(),
                true,
                TemplateId,
                "monthly_income",
                null,
                null,
                null,
                1
            ),
            read,
            versions,
            engine,
            CancellationToken.None
        );

        Assert.Equal(["> 25K", "More than 25K"], page.Items.Select(row => row.ValueLabel));
        Assert.All(page.Items, row => Assert.Equal("option1", row.ValueText));
    }

    private static FormTemplateVersion Version(
        TaxVision.DataCollection.Engine.IFormEngine engine,
        int number,
        string json
    )
    {
        var canonical = engine.Canonicalize(engine.ParseDefinition(json).Value);
        return FormTemplateVersion.Create(
            TenantId,
            TemplateId,
            number,
            "Primer Template",
            TaxVision.Tasks.ClientForms.Domain.Templates.FormPurpose.ClientIntake,
            TaxVision.Tasks.ClientForms.Domain.Templates.FormPresentation.SinglePage,
            ClientFormsTestDocuments.Definition(canonical.Json),
            ClientFormsTestDocuments.Hash(canonical.Sha256),
            3,
            Guid.NewGuid(),
            Now
        );
    }

    private static FormReportAnswerRow Row(int version, string value) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Manuel D Mena",
            "Information Request",
            1,
            Now,
            "monthly_income",
            null,
            value,
            null,
            null,
            version,
            value
        );

    private sealed class FixedRows(params FormReportAnswerRow[] rows) : IFormAnswerIndexReadRepository
    {
        public Task<PagedResult<FormReportAnswerRow>> SearchAsync(
            FormReportAnswersQuery query,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                new PagedResult<FormReportAnswerRow>(rows, 1, FormReportAnswersHandler.PageSize, rows.Length)
            );

        public Task<IReadOnlyList<FormReportSummaryBucket>> SummarizeAsync(
            FormReportSummaryQuery query,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FormReportSummaryBucket>>([]);

        public Task<IReadOnlyList<FormReportExportEntry>> ExportRowsAsync(
            FormReportExportQuery query,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FormReportExportEntry>>([]);
    }
}
