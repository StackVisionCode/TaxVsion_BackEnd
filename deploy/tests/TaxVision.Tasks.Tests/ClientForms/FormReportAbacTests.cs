using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Reporting;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Domain.Projections;
using TaxVision.Tasks.Infrastructure.Persistence;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

// Los reportes solo muestran clientes visibles: cliente asignado, o externo del que el actor es
// responsable/solicitante. CanViewAll entra a todo.
public sealed class FormReportAbacTests
{
    private const string RepositoryNamespace = "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.";
    private static readonly Guid TemplateId = Guid.Parse("30000000-0000-0000-0000-00000000ab01");
    private static readonly Guid AssignedCustomer = Guid.Parse("40000000-0000-0000-0000-00000000ab01");
    private static readonly Guid OtherCustomer = Guid.Parse("40000000-0000-0000-0000-00000000ab02");
    private static readonly Guid AssignedEmployee = Guid.Parse("50000000-0000-0000-0000-00000000ab01");
    private static readonly Guid Responsible = Guid.Parse("50000000-0000-0000-0000-00000000ab02");
    private static readonly Guid Stranger = Guid.Parse("50000000-0000-0000-0000-00000000ab03");
    private static readonly Guid ParticipantId = Guid.Parse("60000000-0000-0000-0000-00000000ab01");

    [Fact]
    public async Task Search_shows_only_the_customers_assigned_to_the_actor()
    {
        await using var db = await SeedAsync();

        var rows = await Read(db).SearchAsync(Answers(AssignedEmployee, false));

        var row = Assert.Single(rows.Items);
        Assert.Equal(AssignedCustomer, row.CustomerId);
        Assert.Equal("Yes", row.ValueText);
    }

    [Fact]
    public async Task Search_shows_an_external_request_to_its_responsible()
    {
        await using var db = await SeedAsync();

        var rows = await Read(db).SearchAsync(Answers(Responsible, false));

        var row = Assert.Single(rows.Items);
        Assert.Equal(ParticipantId, row.ParticipantId);
        Assert.Equal("No", row.ValueText);
    }

    [Fact]
    public async Task Search_hides_everything_from_an_unrelated_actor()
    {
        await using var db = await SeedAsync();

        var rows = await Read(db).SearchAsync(Answers(Stranger, false));

        Assert.Empty(rows.Items);
    }

    [Fact]
    public async Task View_all_sees_every_customer_and_can_filter_by_value()
    {
        await using var db = await SeedAsync();

        var all = await Read(db).SearchAsync(Answers(Stranger, true));
        var onlyYes = await Read(db).SearchAsync(Answers(Stranger, true) with { Value = "Yes" });

        Assert.Equal(3, all.TotalCount);
        Assert.Equal(2, onlyYes.TotalCount);
    }

    [Fact]
    public async Task Summary_counts_per_value_respect_the_abac()
    {
        await using var db = await SeedAsync();

        var all = await Read(db)
            .SummarizeAsync(new FormReportSummaryQuery(TenantId, Stranger, true, TemplateId, "crypto"));
        var mine = await Read(db)
            .SummarizeAsync(new FormReportSummaryQuery(TenantId, AssignedEmployee, false, TemplateId, "crypto"));

        Assert.Equal(("Yes", 2), (all[0].Value, all[0].Count));
        Assert.Equal(("No", 1), (all[1].Value, all[1].Count));
        var bucket = Assert.Single(mine);
        Assert.Equal(("Yes", 1), (bucket.Value, bucket.Count));
    }

    [Fact]
    public async Task Export_rows_follow_the_abac()
    {
        await using var db = await SeedAsync();

        var all = await Read(db)
            .ExportRowsAsync(new FormReportExportQuery(TenantId, Stranger, true, TemplateId, null, null));
        var mine = await Read(db)
            .ExportRowsAsync(new FormReportExportQuery(TenantId, AssignedEmployee, false, TemplateId, null, null));

        Assert.Equal(3, all.Select(entry => entry.FormRequestId).Distinct().Count());
        var row = Assert.Single(mine);
        Assert.Equal("Yes", row.ValueText);
    }

    private static FormReportAnswersQuery Answers(Guid actor, bool canViewAll) =>
        new(TenantId, actor, canViewAll, TemplateId, "crypto", null, null, null, 1);

    private static async Task<TasksDbContext> SeedAsync()
    {
        var db = NewContext();
        var version = Version();
        db.FormTemplateVersions.Add(version);

        var external = ExternalRequest(version);
        db.FormRequests.Add(external);

        db.CustomerAssignmentProjections.Add(
            CustomerAssignmentProjection.Create(TenantId, AssignedCustomer, AssignedEmployee, Now)
        );

        db.FormAnswerIndexEntries.Add(Entry(Guid.NewGuid(), AssignedCustomer, null, "Yes"));
        db.FormAnswerIndexEntries.Add(Entry(Guid.NewGuid(), OtherCustomer, null, "Yes"));
        db.FormAnswerIndexEntries.Add(Entry(external.Id, null, ParticipantId, "No"));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static FormAnswerIndexEntry Entry(Guid requestId, Guid? customerId, Guid? participantId, string value) =>
        FormAnswerIndexEntry.Create(
            TenantId,
            requestId,
            TemplateId,
            1,
            customerId,
            participantId,
            1,
            "crypto",
            null,
            value,
            null,
            null,
            Now
        );

    private static FormRequest ExternalRequest(
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion version
    ) =>
        FormRequest
            .Create(
                TenantId,
                FormParticipantRef.External(ParticipantId),
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
                Responsible,
                Responsible,
                Now
            )
            .Value;

    private static IFormAnswerIndexReadRepository Read(TasksDbContext db) =>
        (IFormAnswerIndexReadRepository)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(RepositoryNamespace + "FormAnswerIndexReadRepository", true)!,
                db
            )!;

    private static TasksDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
