using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Insights;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace TaxVision.Tasks.Tests.ClientForms;

// Insights: conteos por cohorte y período, medianas, embudo, serie, automatizaciones y firmas, con el ABAC
// de la bandeja. El SQL real se genera sin conectarse para cubrir la traducción de cada consulta.
public sealed class FormInsightsReadRepositoryTests(ITestOutputHelper output)
{
    private const string RepositoryNamespace = "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.";
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Admin = Guid.Parse("20000000-0000-0000-0000-0000000000e1");
    private static readonly Guid Employee = Guid.Parse("20000000-0000-0000-0000-0000000000e2");
    private static readonly Guid AssignedCustomer = Guid.Parse("30000000-0000-0000-0000-0000000000e1");
    private static readonly DateTime Now = TruncateToMinute(DateTime.UtcNow);

    [Fact]
    public async Task Kpis_count_the_cohort_sent_in_each_period()
    {
        await using var db = await SeedAsync();

        var summary = await SummaryAsync(db, Admin, canViewAll: true);

        Assert.Equal(new FormInsightsKpisResponse(5, 4, 3, 2, 1, 0.4, 180, 1.0), summary.Current);
        Assert.Equal(new FormInsightsKpisResponse(2, 1, 1, 1, 0, 0.5, 30, 0.0), summary.Previous);
        Assert.Equal(1, summary.OverdueOpen);
        Assert.True(summary.HasAnyRequests);
    }

    [Fact]
    public async Task The_funnel_converts_each_step_from_the_previous_one()
    {
        await using var db = await SeedAsync();

        var funnel = (await SummaryAsync(db, Admin, canViewAll: true)).Funnel;

        Assert.Equal(["sent", "started", "submitted", "completed"], funnel.Select(step => step.Key).ToArray());
        Assert.Equal([5, 4, 3, 2], funnel.Select(step => step.Count).ToArray());
        Assert.Equal([null, 0.8, 0.75, 0.6667], funnel.Select(step => step.ConversionFromPrevious).ToArray());
    }

    [Fact]
    public async Task The_trend_has_one_point_per_local_day_and_only_counts_the_current_period()
    {
        await using var db = await SeedAsync();

        var summary = await SummaryAsync(db, Admin, canViewAll: true);

        Assert.Equal(30, summary.Trend.Count);
        Assert.Equal(DateOnly.FromDateTime(Now).AddDays(-29), summary.Trend[0].Start);
        Assert.Equal(5, summary.Trend.Sum(point => point.Sent));
        Assert.Equal(2, summary.Trend.Sum(point => point.Completed));
        Assert.Equal(1, summary.Trend.Single(point => point.Start == DateOnly.FromDateTime(Now.AddDays(-5))).Sent);
        Assert.Equal(FormInsightsGranularity.Day, summary.Period.Granularity);
    }

    [Fact]
    public async Task Ninety_days_are_grouped_by_week()
    {
        await using var db = await SeedAsync();

        var summary = await SummaryAsync(db, Admin, canViewAll: true, period: 90);

        Assert.Equal(FormInsightsGranularity.Week, summary.Period.Granularity);
        Assert.Equal(13, summary.Trend.Count);
        // Las 5 actuales, las 2 del período anterior de 30 días y la de hace 80 días; la de captación nunca.
        Assert.Equal(8, summary.Trend.Sum(point => point.Sent));
    }

    [Fact]
    public async Task Automation_and_signatures_tally_the_latest_runs_and_revisions_of_the_period()
    {
        await using var db = await SeedAsync();

        var summary = await SummaryAsync(db, Admin, canViewAll: true);

        var automation = summary.Automation.ToDictionary(item => item.Kind);
        Assert.Equal(new FormInsightsAutomationResponse("CreateCustomer", 0, 1, 1, 0, 0), automation["CreateCustomer"]);
        Assert.Equal(
            new FormInsightsAutomationResponse("UpdateCustomerProfile", 1, 0, 0, 0, 0),
            automation["UpdateCustomerProfile"]
        );
        Assert.Equal(new FormInsightsAutomationResponse("SummaryPdf", 1, 1, 0, 0, 1), automation["SummaryPdf"]);
        Assert.Equal(new FormInsightsAutomationResponse("Signature", 1, 1, 0, 0, 0), automation["Signature"]);
        Assert.Equal(new FormInsightsSignaturesResponse(2, 1, 0, 0, 0, 0, 1), summary.Signatures);
        Assert.Equal(
            [
                ("CreateCustomer", FormActionRunErrors.DuplicateFoundCode),
                ("CreateCustomer", "payload_invalid"),
                ("Signature", FormInsightsAssembler.SignatureFailedCode),
                ("SummaryPdf", FormInsightsAssembler.PdfFailedCode),
            ],
            summary.TopFailureReasons.Select(item => (item.Kind, item.Code)).ToArray()
        );
    }

    [Fact]
    public async Task The_split_separates_client_and_external_requests()
    {
        await using var db = await SeedAsync();

        var split = (await SummaryAsync(db, Admin, canViewAll: true)).ParticipantSplit;

        Assert.Equal(
            [
                new FormInsightsParticipantSplitResponse(FormParticipantKind.Customer, 3, 2),
                new FormInsightsParticipantSplitResponse(FormParticipantKind.External, 2, 0),
            ],
            split
        );
    }

    [Fact]
    public async Task The_template_filter_narrows_every_metric_but_keeps_all_template_options()
    {
        await using var db = await SeedAsync();
        var annual = await TemplateIdAsync(db, "Annual intake");

        var summary = await SummaryAsync(db, Admin, canViewAll: true, templateId: annual);

        Assert.Equal(2, summary.Current.Sent);
        Assert.Equal(0, summary.OverdueOpen);
        Assert.Equal(["Annual intake", "Engagement letter"], summary.Templates.Select(item => item.Name).ToArray());
    }

    [Fact]
    public async Task An_employee_only_counts_the_requests_the_inbox_shows_them()
    {
        await using var db = await SeedAsync();

        var summary = await SummaryAsync(db, Employee, canViewAll: false);

        // Externa propia (c3) y la de cliente asignado (c1); nada más.
        Assert.Equal(2, summary.Current.Sent);
        Assert.Equal(1, summary.Current.Completed);
        Assert.Equal(1, summary.OverdueOpen);
        Assert.Equal(0, summary.Previous.Sent);
    }

    [Fact]
    public async Task An_office_without_requests_gets_zeros_and_a_full_empty_series()
    {
        await using var db = NewDb();

        var summary = await SummaryAsync(db, Admin, canViewAll: true, period: 7);

        Assert.False(summary.HasAnyRequests);
        Assert.Equal(new FormInsightsKpisResponse(0, 0, 0, 0, 0, null, null, null), summary.Current);
        Assert.Equal(7, summary.Trend.Count);
        Assert.All(summary.Trend, point => Assert.Equal((0, 0), (point.Sent, point.Completed)));
        Assert.Empty(summary.TopFailureReasons);
        Assert.Empty(summary.Templates);
    }

    [Fact]
    public async Task Templates_are_ranked_by_sent_with_rate_median_and_returns()
    {
        await using var db = await SeedAsync();

        var page = await TemplatesAsync(db, page: 1, size: 10);

        Assert.Equal(2, page.TotalCount);
        var engagement = page.Items[0];
        Assert.Equal(("Engagement letter", 3, 0, 0.0, 600, 0), Row(engagement));
        Assert.Equal(("Annual intake", 2, 2, 1.0, 120, 1), Row(page.Items[1]));
    }

    [Fact]
    public async Task Templates_are_paginated()
    {
        await using var db = await SeedAsync();

        var second = await TemplatesAsync(db, page: 2, size: 1);
        var capped = await TemplatesAsync(db, page: 1, size: 500);

        Assert.Equal("Annual intake", Assert.Single(second.Items).Name);
        Assert.Equal(2, second.TotalCount);
        Assert.Equal(FormInsightsTemplatesHandler.MaxSize, capped.Size);
    }

    // InMemory no detecta consultas que SQL Server no sabe traducir: se genera el SQL real sin conectarse.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Every_query_translates_to_sql_server(bool canViewAll, bool withTemplate)
    {
        using var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=translation_only;Trusted_Connection=True;TrustServerCertificate=True"
                )
                .Options,
            new NoTenant()
        );
        var repository = Reads(db);
        var scope = new FormInsightsScope(TenantId, Employee, canViewAll, withTemplate ? Guid.NewGuid() : null);
        var window = FormInsightsWindow.Create(30, -300, Now);
        Guid[] ids = [Guid.NewGuid()];
        var queries = new (string Name, object[] Args)[]
        {
            ("Cohort", [scope, window]),
            ("Durations", [scope, window]),
            ("Overdue", [scope, Now]),
            ("SentByDay", [scope, window]),
            ("CompletedByDay", [scope, window]),
            ("Runs", [scope, window]),
            ("Failures", [scope, window]),
            ("Pdfs", [scope, window]),
            ("Signatures", [scope, window]),
            ("TemplateOptions", [scope]),
            ("TemplateTallies", [scope, window]),
            ("TemplatePage", [scope, window, 2, 10]),
            ("TemplateNames", [TenantId, ids]),
            ("TemplateDurations", [scope, window, ids]),
            ("TemplateReturns", [scope, window, ids]),
        };

        foreach (var (name, args) in queries)
        {
            var sql = Query(repository, name, args).ToQueryString();
            output.WriteLine($"-- {name}\n{sql}\n");
            Assert.Contains("SELECT", sql, StringComparison.Ordinal);
            Assert.Contains("[TenantId]", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Day_buckets_use_the_offset_as_a_constant_so_the_group_key_has_no_parameter()
    {
        using var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>()
                .UseSqlServer("Server=localhost;Database=translation_only;Trusted_Connection=True")
                .Options,
            new NoTenant()
        );
        var window = FormInsightsWindow.Create(30, -300, Now);

        var sql = Query(Reads(db), "SentByDay", [new FormInsightsScope(TenantId, Admin, true, null), window])
            .ToQueryString();

        Assert.Contains("DATEADD(minute, CAST(-300.0E0 AS int)", sql, StringComparison.Ordinal);
    }

    private static (string, int, int, double?, int?, int) Row(FormInsightsTemplateRowResponse row) =>
        (row.Name, row.Sent, row.Completed, row.CompletionRate, row.MedianMinutesToFirstSubmission, row.Returns);

    private static Task<FormInsightsSummaryResponse> SummaryAsync(
        TasksDbContext db,
        Guid actor,
        bool canViewAll,
        int period = 30,
        Guid? templateId = null
    ) =>
        FormInsightsSummaryHandler.Handle(
            new FormInsightsSummaryQuery(TenantId, actor, canViewAll, period, templateId, 0),
            Reads(db),
            default
        );

    private static Task<PagedResult<FormInsightsTemplateRowResponse>> TemplatesAsync(
        TasksDbContext db,
        int page,
        int size
    ) =>
        FormInsightsTemplatesHandler.Handle(
            new FormInsightsTemplatesQuery(TenantId, Admin, true, 30, 0, page, size),
            Reads(db),
            default
        );

    private static async Task<Guid> TemplateIdAsync(TasksDbContext db, string name) =>
        (await db.FormTemplates.IgnoreQueryFilters().ToListAsync()).Single(item => item.Name.Value == name).Id;

    private static async Task<TasksDbContext> SeedAsync()
    {
        var db = NewDb();
        var (_, annualVersion) = Template(db, "Annual intake");
        var (_, engagementVersion) = Template(db, "Engagement letter");

        // c1: cliente asignado, abierto, enviado a los 60 min, completado, 2 recordatorios, PDF listo y firma sellada.
        var c1 = Customer(db, annualVersion, AssignedCustomer, Now.AddDays(-5));
        c1.RecordReminder(5, Now.AddDays(-5).AddMinutes(30));
        c1.RecordReminder(5, Now.AddDays(-5).AddMinutes(40));
        c1.MarkOpened(Now.AddDays(-5).AddMinutes(10));
        Submit(c1, FormActor.PortalUser(Admin), Now.AddDays(-5).AddMinutes(60));
        var pdf = Guid.NewGuid();
        c1.MarkPdfRequested(1, pdf);
        c1.LinkPdf(pdf, Guid.NewGuid());
        c1.MarkSignatureRequesting(1, Guid.NewGuid(), new string('a', 64));
        c1.LinkSignatureRequest(1, Guid.NewGuid());
        c1.MarkSignatureSealed(1, Guid.NewGuid(), new string('b', 64), null);
        c1.Complete(null, Admin, Now.AddDays(-4));

        // c2: devuelto una vez y completado; el PDF de la revisión 1 falló y el de la 2 sigue pedido.
        var c2 = Customer(db, annualVersion, Guid.NewGuid(), Now.AddDays(-4));
        Submit(c2, FormActor.PortalUser(Admin), Now.AddDays(-4).AddMinutes(180));
        var failedPdf = Guid.NewGuid();
        c2.MarkPdfRequested(1, failedPdf);
        c2.MarkPdfFailed(failedPdf);
        c2.ReturnForChanges("Please attach the W-2.", [], Admin, Now.AddDays(-4).AddMinutes(200));
        Submit(c2, FormActor.PortalUser(Admin), Now.AddDays(-4).AddMinutes(260));
        c2.MarkPdfRequested(2, Guid.NewGuid());
        var update = Guid.NewGuid();
        c2.RequestAction("profile", FormActionNames.UpdateCustomerProfile, update, Now.AddDays(-4).AddMinutes(270));
        c2.CompleteAction(update, "Customer", Guid.NewGuid(), null, Now.AddDays(-4).AddMinutes(271));
        c2.Complete(null, Admin, Now.AddDays(-3));

        // c3: externa del empleado, solo borrador y vencida.
        var c3 = External(db, engagementVersion, Employee, Now.AddDays(-3), dueAtUtc: Now.AddDays(-2), out var p3);
        c3.SaveDraft(new ProtectedAnswers("{\"schemaVersion\":1,\"values\":{}}", 1), 40, Actor(p3), Now.AddDays(-3));

        // c4: cliente sin abrir, cancelada.
        var c4 = Customer(db, engagementVersion, Guid.NewGuid(), Now.AddDays(-2));
        c4.Cancel(null, Admin, Now.AddDays(-1));

        // c5: externa enviada a los 600 min; alta de cliente rechazada por duplicado y otra fallida; firma fallida.
        var c5 = External(db, engagementVersion, Admin, Now.AddDays(-1), dueAtUtc: null, out var p5);
        Submit(c5, Actor(p5), Now.AddDays(-1).AddMinutes(600));
        var create = Guid.NewGuid();
        c5.RequestAction("create_client", FormActionNames.CreateCustomer, create, Now.AddDays(-1).AddMinutes(601));
        c5.RejectAction(
            create,
            FormActionRunErrors.DuplicateFoundCode,
            Guid.NewGuid(),
            "Email",
            Now.AddDays(-1).AddMinutes(602)
        );
        c5.CloseActionWithoutCommand(
            "create_other",
            FormActionNames.CreateCustomer,
            FormActionRunStatus.Failed,
            "payload_invalid",
            Now.AddDays(-1).AddMinutes(603)
        );
        c5.MarkSignatureRequesting(1, Guid.NewGuid(), new string('c', 64));
        c5.MarkSignatureFailed(1);

        // Período anterior: una enviada a los 30 min y completada, otra sin actividad.
        var p1 = Customer(db, annualVersion, Guid.NewGuid(), Now.AddDays(-40));
        Submit(p1, FormActor.PortalUser(Admin), Now.AddDays(-40).AddMinutes(30));
        p1.Complete(null, Admin, Now.AddDays(-39));
        Customer(db, annualVersion, Guid.NewGuid(), Now.AddDays(-45));

        // Fuera de toda ventana corta y una de captación (no es un envío de la oficina).
        Customer(db, annualVersion, Guid.NewGuid(), Now.AddDays(-80));
        Intake(db, engagementVersion, Now.AddDays(-2));

        db.CustomerAssignmentProjections.Add(
            CustomerAssignmentProjection.Create(TenantId, AssignedCustomer, Employee, Now.AddDays(-90))
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static (FormTemplate Template, FormTemplateVersion Version) Template(TasksDbContext db, string name)
    {
        var template = FormTemplateHandlerTestDoubles.Template(TenantId);
        template.Rename(FormTemplateName.Create(name).Value, null, Admin, Now.AddDays(-100));
        var version = FormTemplateVersion.Create(
            TenantId,
            template.Id,
            1,
            name,
            FormPurpose.DocumentRequest,
            FormPresentation.SinglePage,
            ClientFormsTestDocuments.Definition(FormRequestFixtures.Definition),
            ClientFormsTestDocuments.Hash(new string('a', 64)),
            0,
            Admin,
            Now.AddDays(-100)
        );
        db.FormTemplates.Add(template);
        db.FormTemplateVersions.Add(version);
        return (template, version);
    }

    private static FormRequest Customer(TasksDbContext db, FormTemplateVersion version, Guid customerId, DateTime at)
    {
        var request = FormRequest
            .Create(
                TenantId,
                FormParticipantRef.Customer(customerId),
                version,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                null,
                Admin,
                at
            )
            .Value;
        db.FormRequests.Add(request);
        return request;
    }

    private static FormRequest External(
        TasksDbContext db,
        FormTemplateVersion version,
        Guid responsible,
        DateTime at,
        DateTime? dueAtUtc,
        out FormParticipant participant
    )
    {
        participant = FormParticipant
            .Create(
                TenantId,
                $"{Guid.NewGuid():N}@example.com",
                "Prospect",
                null,
                FormParticipantSource.DirectedRequest,
                at
            )
            .Value;
        var request = FormRequest
            .Create(
                TenantId,
                FormParticipantRef.External(participant.Id),
                version,
                null,
                null,
                null,
                null,
                dueAtUtc,
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                responsible,
                responsible,
                at
            )
            .Value;
        db.FormParticipants.Add(participant);
        db.FormRequests.Add(request);
        return request;
    }

    private static void Intake(TasksDbContext db, FormTemplateVersion version, DateTime at)
    {
        var participant = FormParticipant
            .Create(TenantId, "intake@example.com", "Lead", null, FormParticipantSource.DirectedRequest, at)
            .Value;
        db.FormParticipants.Add(participant);
        db.FormRequests.Add(
            FormRequest
                .Create(
                    TenantId,
                    FormParticipantRef.External(participant.Id),
                    version,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    FormRequestOrigin.IntakeLink,
                    Guid.NewGuid(),
                    Admin,
                    Admin,
                    at
                )
                .Value
        );
    }

    private static void Submit(FormRequest request, FormActor actor, DateTime at) =>
        Assert.True(
            request.Submit(new ProtectedAnswers("{\"schemaVersion\":1,\"values\":{}}", 1), [], [], actor, at).IsSuccess
        );

    private static FormActor Actor(FormParticipant participant) =>
        FormActor.ExternalSession(participant.Id, Guid.NewGuid());

    private static TasksDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

    private static IFormInsightsReadRepository Reads(TasksDbContext db) =>
        (IFormInsightsReadRepository)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(
                    RepositoryNamespace + "FormInsightsReadRepository",
                    throwOnError: true
                )!,
                db
            )!;

    private static IQueryable Query(IFormInsightsReadRepository repository, string name, object[] args) =>
        (IQueryable)
            repository
                .GetType()
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(repository, args)!;

    private static DateTime TruncateToMinute(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
