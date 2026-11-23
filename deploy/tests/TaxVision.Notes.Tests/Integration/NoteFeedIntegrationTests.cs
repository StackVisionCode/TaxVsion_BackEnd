using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.Projections;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Infrastructure.Persistence;
using TaxVision.Notes.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>Central de notas: el SQL real del feed (visibilidad, asignación, filtros, orden y paginación) y el endpoint.</summary>
public sealed class NoteFeedIntegrationTests(NotesApiFactory factory) : IClassFixture<NotesApiFactory>
{
    private static readonly DateTime BaseUtc = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid employeeA = Guid.NewGuid();
    private readonly Guid employeeB = Guid.NewGuid();

    [Fact]
    public async Task Employee_does_not_see_another_employees_private_notes_unless_view_all()
    {
        var mine = await SeedAsync(employeeA, NoteVisibility.Private);
        var othersPrivate = await SeedAsync(employeeB, NoteVisibility.Private);
        var othersTeam = await SeedAsync(employeeB, NoteVisibility.Team);

        var asEmployee = await FeedIdsAsync(employeeA, hasViewAll: false, new NoteFeedCriteria());
        var asAdmin = await FeedIdsAsync(employeeA, hasViewAll: true, new NoteFeedCriteria());

        AssertSameIds([mine.Id, othersTeam.Id], asEmployee);
        AssertSameIds([mine.Id, othersPrivate.Id, othersTeam.Id], asAdmin);
    }

    [Fact]
    public async Task Scope_filters_mine_team_client_private_and_others_private()
    {
        var minePrivate = await SeedAsync(employeeA, NoteVisibility.Private);
        var mineTeam = await SeedAsync(employeeA, NoteVisibility.Team);
        var othersClient = await SeedAsync(employeeB, NoteVisibility.ClientVisible);
        var othersPrivate = await SeedAsync(employeeB, NoteVisibility.Private);

        AssertSameIds([minePrivate.Id, mineTeam.Id], await ScopeAsync(NoteFeedScope.Mine));
        Assert.Equal([mineTeam.Id], await ScopeAsync(NoteFeedScope.Team));
        Assert.Equal([othersClient.Id], await ScopeAsync(NoteFeedScope.Client));
        Assert.Equal([minePrivate.Id], await ScopeAsync(NoteFeedScope.Private));
        Assert.Equal([othersPrivate.Id], await ScopeAsync(NoteFeedScope.OthersPrivate));
    }

    [Fact]
    public async Task Filters_by_customer_author_target_type_status_and_date_range()
    {
        var customerId = Guid.NewGuid();
        var onCustomer = await SeedAsync(employeeA, NoteVisibility.Team, NoteTargetType.Customer, customerId, 0);
        var otherCustomer = await SeedAsync(employeeB, NoteVisibility.Team, NoteTargetType.Customer, Guid.NewGuid(), 1);
        var onTask = await SeedAsync(employeeB, NoteVisibility.Team, NoteTargetType.Task, Guid.NewGuid(), 2);
        var archived = await SeedAsync(employeeA, NoteVisibility.Team, minutes: 3, archive: true);

        Assert.Equal([onCustomer.Id], await CriteriaAsync(new NoteFeedCriteria(CustomerId: customerId)));
        AssertSameIds(
            [otherCustomer.Id, onTask.Id],
            await CriteriaAsync(new NoteFeedCriteria(AuthorUserId: employeeB))
        );
        Assert.Equal([onTask.Id], await CriteriaAsync(new NoteFeedCriteria(TargetType: NoteTargetType.Task)));
        Assert.Equal([archived.Id], await CriteriaAsync(new NoteFeedCriteria(Status: NoteFeedStatus.Archived)));
        Assert.Equal(
            [otherCustomer.Id],
            await CriteriaAsync(new NoteFeedCriteria(FromUtc: BaseUtc.AddMinutes(1), ToUtc: BaseUtc.AddMinutes(2)))
        );
    }

    [Fact]
    public async Task Sorts_by_updated_created_and_pinned()
    {
        var older = await SeedAsync(employeeA, NoteVisibility.Team, minutes: 0);
        var newer = await SeedAsync(employeeA, NoteVisibility.Team, minutes: 5);
        var pinnedOld = await SeedAsync(employeeA, NoteVisibility.Team, minutes: 1, pin: true);

        // Fijar mueve UpdatedAtUtc al minuto 10: "updated" la pone primero; "created" respeta la creación.
        Assert.Equal(
            [pinnedOld.Id, newer.Id, older.Id],
            await CriteriaAsync(new NoteFeedCriteria(Sort: NoteFeedSort.Updated))
        );
        Assert.Equal(
            [newer.Id, pinnedOld.Id, older.Id],
            await CriteriaAsync(new NoteFeedCriteria(Sort: NoteFeedSort.Created))
        );
        Assert.Equal(pinnedOld.Id, (await CriteriaAsync(new NoteFeedCriteria(Sort: NoteFeedSort.Pinned)))[0]);
    }

    [Fact]
    public async Task Search_finds_a_word_written_after_the_preview_limit()
    {
        var deep = await SeedAsync(employeeA, NoteVisibility.Team, html: $"<p>{new string('x', 300)} remolacha</p>");
        await SeedAsync(employeeA, NoteVisibility.Team, html: "<p>otra cosa</p>");

        Assert.Equal([deep.Id], await CriteriaAsync(new NoteFeedCriteria(Term: "remolacha")));
    }

    [Fact]
    public async Task Notes_of_another_tenant_never_appear()
    {
        var own = await SeedAsync(employeeA, NoteVisibility.Team);
        await SeedAsync(employeeA, NoteVisibility.Team, tenant: Guid.NewGuid());

        Assert.Equal([own.Id], await FeedIdsAsync(employeeA, hasViewAll: true, new NoteFeedCriteria()));
    }

    [Fact]
    public async Task Assignment_filter_hides_notes_of_unassigned_customers()
    {
        var assignedCustomer = Guid.NewGuid();
        var assigned = await SeedAsync(employeeB, NoteVisibility.Team, NoteTargetType.Customer, assignedCustomer);
        await SeedAsync(employeeB, NoteVisibility.Team, NoteTargetType.Customer, Guid.NewGuid());
        var general = await SeedAsync(employeeB, NoteVisibility.Team);
        await using (var db = NewDb())
        {
            db.CustomerAssignmentProjections.Add(
                CustomerAssignmentProjection.Create(tenantId, assignedCustomer, employeeA, BaseUtc)
            );
            await db.SaveChangesAsync();
        }

        var ids = await FeedIdsAsync(employeeA, hasViewAll: false, new NoteFeedCriteria(), assignedTo: employeeA);

        AssertSameIds([assigned.Id, general.Id], ids);
    }

    // "My notes" = solo mis privadas, también las de un cliente que ya no tengo asignado; nada Team ni Client-visible.
    [Fact]
    public async Task My_notes_are_only_my_private_notes_including_unassigned_customers()
    {
        var privateOfUnassigned = await SeedAsync(
            employeeA,
            NoteVisibility.Private,
            NoteTargetType.Customer,
            Guid.NewGuid()
        );
        var privateGeneral = await SeedAsync(employeeA, NoteVisibility.Private);
        await SeedAsync(employeeA, NoteVisibility.Team);
        await SeedAsync(employeeA, NoteVisibility.ClientVisible, NoteTargetType.Customer, Guid.NewGuid());
        await SeedAsync(employeeB, NoteVisibility.Private);

        var ids = await FeedIdsAsync(
            employeeA,
            hasViewAll: false,
            new NoteFeedCriteria(Scope: NoteFeedScope.Private),
            assignedTo: employeeA
        );

        AssertSameIds([privateOfUnassigned.Id, privateGeneral.Id], ids);
    }

    [Fact]
    public async Task Paginates_on_the_server()
    {
        for (var i = 0; i < 5; i++)
            await SeedAsync(employeeA, NoteVisibility.Team, minutes: i);

        await using var db = NewDb();
        var repo = new NoteRepository(db);
        var page2 = await repo.ListFeedAsync(tenantId, employeeA, false, null, new NoteFeedCriteria(), 2, 2);
        var page3 = await repo.ListFeedAsync(tenantId, employeeA, false, null, new NoteFeedCriteria(), 3, 2);

        Assert.Equal(5, page2.TotalCount);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);
    }

    [Fact]
    public async Task Customer_name_comes_from_the_local_directory_and_attachments_are_loaded()
    {
        var customerId = Guid.NewGuid();
        var note = await SeedAsync(employeeA, NoteVisibility.Team, NoteTargetType.Customer, customerId, attach: true);
        await SeedDirectoryAsync(customerId, "Ana Pérez");

        await using var db = NewDb();
        var page = await new NoteRepository(db).ListFeedAsync(
            tenantId,
            employeeA,
            false,
            null,
            new NoteFeedCriteria(),
            1,
            20
        );

        var row = Assert.Single(page.Items);
        Assert.Equal(note.Id, row.Note.Id);
        Assert.Equal("Ana Pérez", row.CustomerName);
        Assert.Single(row.Note.Attachments);
    }

    [Fact]
    public async Task Feed_endpoint_returns_customer_name_and_rejects_an_unknown_scope()
    {
        var customerId = Guid.NewGuid();
        var note = await SeedAsync(employeeA, NoteVisibility.Team, NoteTargetType.Customer, customerId);
        await SeedDirectoryAsync(customerId, "Luis Gómez");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestTokenFactory.Mint(factory, tenantId, employeeA)
        );

        var response = await client.GetAsync($"/notes/feed?customerId={customerId}&scope=all&sort=pinned&size=10");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(note.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("Luis Gómez", item.GetProperty("customerName").GetString());

        var invalid = await client.GetAsync("/notes/feed?scope=bogus");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static void AssertSameIds(Guid[] expected, List<Guid> actual) =>
        Assert.Equal(expected.Order(), actual.Order());

    private Task<List<Guid>> ScopeAsync(NoteFeedScope scope) =>
        FeedIdsAsync(employeeA, hasViewAll: true, new NoteFeedCriteria(Scope: scope));

    private Task<List<Guid>> CriteriaAsync(NoteFeedCriteria criteria) =>
        FeedIdsAsync(employeeA, hasViewAll: true, criteria);

    private async Task<List<Guid>> FeedIdsAsync(
        Guid actor,
        bool hasViewAll,
        NoteFeedCriteria criteria,
        Guid? assignedTo = null
    )
    {
        await using var db = NewDb();
        var page = await new NoteRepository(db).ListFeedAsync(
            tenantId,
            actor,
            hasViewAll,
            assignedTo,
            criteria,
            1,
            100
        );
        return page.Items.Select(r => r.Note.Id).ToList();
    }

    private async Task<Note> SeedAsync(
        Guid author,
        NoteVisibility visibility,
        NoteTargetType targetType = NoteTargetType.None,
        Guid? targetId = null,
        int minutes = 0,
        bool archive = false,
        bool pin = false,
        bool attach = false,
        string html = "<p>nota</p>",
        Guid? tenant = null
    )
    {
        var at = BaseUtc.AddMinutes(minutes);
        var note = Note.Create(
            tenant ?? tenantId,
            author,
            NoteContent.Create(html).Value,
            NoteReference.Create(targetType, targetId).Value,
            visibility,
            null,
            at
        ).Value;
        if (archive)
            note.Archive(author, at);
        if (pin)
            note.Pin(author, BaseUtc.AddMinutes(10));
        if (attach)
            note.AttachFile(Guid.NewGuid(), "recibo.pdf", "application/pdf", 1024, at);

        await using var db = NewDb();
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return note;
    }

    private async Task SeedDirectoryAsync(Guid customerId, string name)
    {
        await using var db = NewDb();
        db.CustomerDirectoryEntries.Add(
            CustomerDirectoryEntry.Create(tenantId, customerId, name, CustomerDirectoryStatus.Active, BaseUtc)
        );
        await db.SaveChangesAsync();
    }

    private static NotesDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<NotesDbContext>()
                .UseSqlServer(NotesIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
