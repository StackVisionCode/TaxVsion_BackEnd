using System.Reflection;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Prospects;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Infrastructure.Persistence;
using static TaxVision.Tasks.Tests.ClientForms.FormActionTestDoubles;

namespace TaxVision.Tasks.Tests.ClientForms;

// Prospectos: estado derivado, ABAC como la bandeja de externas, búsqueda, paginado y perdido/reabierto.
public sealed class ProspectListTests
{
    private const string RepositoryNamespace = "TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories.";
    private static readonly Guid Employee = Guid.Parse("40000000-0000-0000-0000-0000000000c1");
    private static readonly Guid AssignedEmployee = Guid.Parse("40000000-0000-0000-0000-0000000000c2");
    private static readonly Guid ExistingCustomer = Guid.Parse("30000000-0000-0000-0000-0000000000c1");
    private static readonly Guid LinkedCustomer = Guid.Parse("30000000-0000-0000-0000-0000000000c2");

    [Fact]
    public async Task Each_prospect_gets_the_status_of_the_first_rule_that_applies()
    {
        await using var db = await SeedAsync();

        var page = await ListAsync(db, Responsible, canViewAll: true);

        var statuses = page.Items.ToDictionary(item => item.Email, item => item.Status);
        Assert.Equal(ProspectStatus.Invited, statuses["invited@example.com"]);
        Assert.Equal(ProspectStatus.Verified, statuses["verified@example.com"]);
        Assert.Equal(ProspectStatus.Submitted, statuses["submitted@example.com"]);
        Assert.Equal(ProspectStatus.Duplicate, statuses["duplicate@example.com"]);
        Assert.Equal(ProspectStatus.Converted, statuses["converted@example.com"]);
        Assert.Equal(ProspectStatus.Lost, statuses["lost@example.com"]);
        Assert.Equal(6, page.TotalCount);
    }

    [Fact]
    public async Task The_item_carries_the_contract_fields()
    {
        await using var db = await SeedAsync();

        var page = await ListAsync(db, Responsible, canViewAll: true);

        var submitted = page.Items.Single(item => item.Email == "submitted@example.com");
        Assert.Equal("Submitted Person", submitted.FullName);
        Assert.Equal(FormParticipantSource.DirectedRequest, submitted.Source);
        Assert.Equal(1, submitted.RequestCount);
        Assert.Equal("Client onboarding", submitted.LatestRequest!.TemplateName);
        Assert.Equal(FormRequestStatus.Submitted, submitted.LatestRequest.Status);
        Assert.True(submitted.CanCreateClient);
        Assert.Equal(submitted.LatestRequest.Id, submitted.CreateClientRequestId);
        var converted = page.Items.Single(item => item.Email == "converted@example.com");
        Assert.Equal(LinkedCustomer, converted.CustomerId);
        Assert.False(converted.CanCreateClient);
        var duplicate = page.Items.Single(item => item.Email == "duplicate@example.com");
        Assert.Equal(ExistingCustomer, duplicate.DuplicateOfCustomerId);
        Assert.False(duplicate.CanCreateClient);
        Assert.Equal("Not interested", page.Items.Single(item => item.Email == "lost@example.com").LostReason);
    }

    [Fact]
    public async Task Without_the_start_permissions_no_prospect_offers_creating_a_client()
    {
        await using var db = await SeedAsync();

        var page = await ListAsync(db, Responsible, canViewAll: true, canStart: false);

        Assert.All(page.Items, item => Assert.False(item.CanCreateClient));
    }

    [Fact]
    public async Task An_employee_sees_only_the_prospects_whose_requests_they_are_responsible_for()
    {
        await using var db = await SeedAsync();
        var own = Seed(db, "own@example.com", "Own", responsible: Employee);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = await ListAsync(db, Employee, canViewAll: false);

        var item = Assert.Single(page.Items);
        Assert.Equal(own.Id, item.ParticipantId);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task A_converted_prospect_is_also_visible_through_the_client_assignment()
    {
        await using var db = await SeedAsync();
        db.CustomerAssignmentProjections.Add(
            CustomerAssignmentProjection.Create(TenantId, LinkedCustomer, AssignedEmployee, Now)
        );
        await db.SaveChangesAsync();

        var page = await ListAsync(db, AssignedEmployee, canViewAll: false);

        Assert.Equal("converted@example.com", Assert.Single(page.Items).Email);
    }

    [Fact]
    public async Task The_duplicate_client_is_hidden_without_abac_on_it()
    {
        await using var db = await SeedAsync();
        var mine = Seed(db, "mine-dup@example.com", "Mine", responsible: Employee, duplicate: true);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var hidden = Assert.Single((await ListAsync(db, Employee, canViewAll: false)).Items);

        Assert.Equal(mine.Id, hidden.ParticipantId);
        Assert.Equal(ProspectStatus.Duplicate, hidden.Status);
        Assert.Null(hidden.DuplicateOfCustomerId);
    }

    [Theory]
    [InlineData("SUBMITTED@", "submitted@example.com")]
    [InlineData("person", "submitted@example.com")]
    [InlineData("DUPLICATE p", "duplicate@example.com")]
    public async Task Search_matches_email_or_name_ignoring_case(string search, string expected)
    {
        await using var db = await SeedAsync();

        var page = await ListAsync(db, Responsible, canViewAll: true, search: search);

        Assert.Contains(page.Items, item => item.Email == expected);
        Assert.All(
            page.Items,
            item =>
                Assert.True(
                    item.Email.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                        || (item.FullName ?? string.Empty).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                )
        );
    }

    [Fact]
    public async Task The_status_filter_counts_and_returns_only_that_status()
    {
        await using var db = await SeedAsync();

        var page = await ListAsync(db, Responsible, canViewAll: true, status: ProspectStatus.Submitted);

        Assert.Equal("submitted@example.com", Assert.Single(page.Items).Email);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Pages_are_ordered_by_last_activity_and_capped_at_one_hundred()
    {
        await using var db = await SeedAsync();

        var first = await ListAsync(db, Responsible, canViewAll: true, page: 1, size: 4);
        var second = await ListAsync(db, Responsible, canViewAll: true, page: 2, size: 4);
        var capped = await ListAsync(db, Responsible, canViewAll: true, size: 500);

        Assert.Equal(4, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(6, first.TotalCount);
        Assert.Equal(100, capped.Size);
        var all = first.Items.Concat(second.Items).ToArray();
        Assert.Equal(
            all.OrderByDescending(item => item.LastActivityAtUtc).Select(item => item.ParticipantId),
            all.Select(item => item.ParticipantId)
        );
        Assert.Equal(6, all.Select(item => item.ParticipantId).Distinct().Count());
    }

    [Fact]
    public async Task Marking_lost_and_reopening_returns_the_updated_item_and_audits_both()
    {
        await using var db = await SeedAsync();
        var submitted = await db
            .FormParticipants.IgnoreQueryFilters()
            .SingleAsync(item => item.Email == "submitted@example.com");
        db.ChangeTracker.Clear();
        var audit = new ExtAuditLog();

        var lost = await LostAsync(db, submitted.Id, "  Went with another office ", audit);
        var reopened = await ReopenAsync(db, submitted.Id, audit);

        Assert.True(lost.IsSuccess);
        Assert.Equal(ProspectStatus.Lost, lost.Value.Status);
        Assert.Equal("Went with another office", lost.Value.LostReason);
        Assert.True(reopened.IsSuccess);
        Assert.Equal(ProspectStatus.Submitted, reopened.Value.Status);
        Assert.Null(reopened.Value.LostReason);
        Assert.Equal(
            [MarkProspectLostHandler.AuditAction, ReopenProspectHandler.AuditAction],
            audit.Entries.Select(entry => entry.Action).ToArray()
        );
        Assert.DoesNotContain(audit.Entries, entry => entry.Details!.Contains("office", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_converted_prospect_cannot_be_marked_lost()
    {
        await using var db = await SeedAsync();
        var converted = await db
            .FormParticipants.IgnoreQueryFilters()
            .SingleAsync(item => item.Email == "converted@example.com");
        db.ChangeTracker.Clear();

        var result = await LostAsync(db, converted.Id, null, new ExtAuditLog());

        Assert.Equal(FormParticipantErrors.AlreadyConverted, result.Error);
        Assert.Equal("FormParticipant.AlreadyConverted", result.Error.Code);
    }

    [Fact]
    public async Task The_lost_reason_is_limited_and_guarded_like_other_staff_texts()
    {
        await using var db = await SeedAsync();
        var invited = await db
            .FormParticipants.IgnoreQueryFilters()
            .SingleAsync(item => item.Email == "invited@example.com");
        db.ChangeTracker.Clear();

        var tooLong = await LostAsync(db, invited.Id, new string('x', 201), new ExtAuditLog());
        var sensitive = await LostAsync(db, invited.Id, "SSN 123-45-6789", new ExtAuditLog());

        Assert.Equal(FormParticipantErrors.InvalidLostReason, tooLong.Error);
        Assert.Equal(StaffTextGuard.SensitiveData, sensitive.Error);
    }

    [Fact]
    public async Task A_prospect_the_actor_cannot_see_answers_not_found()
    {
        await using var db = await SeedAsync();
        var invited = await db
            .FormParticipants.IgnoreQueryFilters()
            .SingleAsync(item => item.Email == "invited@example.com");
        db.ChangeTracker.Clear();

        var result = await MarkProspectLostHandler.Handle(
            new MarkProspectLostCommand(TenantId, Employee, false, true, invited.Id, null, Now),
            Reads(db),
            Participants(db),
            Mapper(db),
            new ExtAuditLog(),
            db,
            default
        );

        Assert.Equal(FormParticipantErrors.NotFound, result.Error);
    }

    // InMemory no detecta consultas que SQL Server no sabe traducir: se genera el SQL real sin conectarse.
    [Theory]
    [InlineData(false, null, null)]
    [InlineData(true, ProspectStatus.Duplicate, "ada")]
    [InlineData(false, ProspectStatus.Lost, "a_b%")]
    public void The_list_query_translates_to_sql_server(bool canViewAll, ProspectStatus? status, string? search)
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
        var query = new ListProspectsQuery(TenantId, Employee, canViewAll, true, status, search, 2, 25);
        var type = repository.GetType();

        var filtered = (IQueryable)
            type.GetMethod("Filtered", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(repository, [query])!;
        var paged = (IQueryable)
            type.GetMethod("Paged", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [filtered, query])!;

        Assert.Contains("FormParticipants", paged.ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("SELECT", filtered.ToQueryString(), StringComparison.Ordinal);
    }

    private static Task<ProspectPageResponse> ListAsync(
        TasksDbContext db,
        Guid actor,
        bool canViewAll,
        bool canStart = true,
        ProspectStatus? status = null,
        string? search = null,
        int page = 1,
        int size = 25
    ) =>
        ListProspectsHandler.Handle(
            new ListProspectsQuery(TenantId, actor, canViewAll, canStart, status, search, page, size),
            Reads(db),
            Mapper(db),
            default
        );

    private static Task<Result<ProspectResponse>> LostAsync(
        TasksDbContext db,
        Guid participantId,
        string? reason,
        ExtAuditLog audit
    ) =>
        MarkProspectLostHandler.Handle(
            new MarkProspectLostCommand(TenantId, Responsible, true, true, participantId, reason, Now.AddHours(1)),
            Reads(db),
            Participants(db),
            Mapper(db),
            audit,
            db,
            default
        );

    private static Task<Result<ProspectResponse>> ReopenAsync(
        TasksDbContext db,
        Guid participantId,
        ExtAuditLog audit
    ) =>
        ReopenProspectHandler.Handle(
            new ReopenProspectCommand(TenantId, Responsible, true, true, participantId, Now.AddHours(2)),
            Reads(db),
            Participants(db),
            Mapper(db),
            audit,
            db,
            default
        );

    private static async Task<TasksDbContext> SeedAsync()
    {
        var db = new TasksDbContext(
            new DbContextOptionsBuilder<TasksDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        Seed(db, "invited@example.com", "Invited Person", verified: false, minutes: 1);
        Seed(db, "verified@example.com", "Verified Person", minutes: 2);
        Seed(db, "submitted@example.com", "Submitted Person", submitted: true, minutes: 3);
        Seed(db, "duplicate@example.com", "Duplicate Person", duplicate: true, minutes: 4);
        var converted = Seed(db, "converted@example.com", "Converted Person", submitted: true, minutes: 5);
        converted.LinkToCustomer(LinkedCustomer, Responsible, Now);
        var lost = Seed(db, "lost@example.com", "Lost Person", minutes: 6);
        lost.MarkLost("Not interested", Responsible, Now.AddMinutes(30));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static FormParticipant Seed(
        TasksDbContext db,
        string email,
        string name,
        bool verified = true,
        bool submitted = false,
        bool duplicate = false,
        int minutes = 0,
        Guid? responsible = null
    )
    {
        var participant = FormParticipant
            .Create(TenantId, email, name, null, FormParticipantSource.DirectedRequest, Now)
            .Value;
        if (verified)
            participant.MarkVerified(Now);
        var version = VersionFor(db);
        var request = FormRequest
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
                FormRequestOrigin.Directed,
                null,
                responsible ?? Responsible,
                responsible ?? Responsible,
                Now.AddMinutes(minutes)
            )
            .Value;
        if (submitted || duplicate)
            Submit(request, participant);
        if (duplicate)
        {
            var command = Guid.NewGuid();
            request.RequestAction("create_client", FormActionNames.CreateCustomer, command, Now);
            request.RejectAction(command, FormActionRunErrors.DuplicateFoundCode, ExistingCustomer, "Email", Now);
        }
        db.FormParticipants.Add(participant);
        db.FormRequests.Add(request);
        return participant;
    }

    private static readonly FormTemplateVersion SharedVersion = Version();

    private static FormTemplateVersion VersionFor(TasksDbContext db)
    {
        if (!db.FormTemplateVersions.Local.Any() && !db.FormTemplateVersions.IgnoreQueryFilters().Any())
            db.FormTemplateVersions.Add(SharedVersion);
        return SharedVersion;
    }

    private static IProspectReadRepository Reads(TasksDbContext db) =>
        Create<IProspectReadRepository>("ProspectReadRepository", db);

    private static IFormParticipantRepository Participants(TasksDbContext db) =>
        Create<IFormParticipantRepository>("FormParticipantRepository", db);

    private static ProspectResponseMapper Mapper(TasksDbContext db) =>
        new(new MemoryFormTemplateVersionRepository(SharedVersion), FormTemplateHandlerTestDoubles.Engine());

    private static T Create<T>(string typeName, TasksDbContext db) =>
        (T)
            Activator.CreateInstance(
                typeof(TasksDbContext).Assembly.GetType(RepositoryNamespace + typeName, throwOnError: true)!,
                db
            )!;

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
