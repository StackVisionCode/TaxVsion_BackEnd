using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Infrastructure.Persistence;
using TaxVision.Notes.Infrastructure.Persistence.Repositories;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>Títulos en vivo contra el SQL real: mismo predicado de visibilidad y asignación que el feed.</summary>
public sealed class NoteWorkTitlesIntegrationTests
{
    private static readonly DateTime BaseUtc = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid viewer = Guid.NewGuid();
    private readonly Guid colleague = Guid.NewGuid();

    [Fact]
    public async Task Private_notes_of_others_appear_only_with_view_all()
    {
        var mine = await SeedAsync(viewer, NoteVisibility.Private);
        var othersPrivate = await SeedAsync(colleague, NoteVisibility.Private);
        var othersTeam = await SeedAsync(colleague, NoteVisibility.Team);
        Guid[] ids = [mine.Id, othersPrivate.Id, othersTeam.Id];

        AssertSameIds([mine.Id, othersTeam.Id], await TitleIdsAsync(ids, hasViewAll: false));
        AssertSameIds([mine.Id, othersPrivate.Id, othersTeam.Id], await TitleIdsAsync(ids, hasViewAll: true));
    }

    [Fact]
    public async Task Notes_of_unassigned_customers_are_omitted_with_the_assignment_filter()
    {
        var assignedCustomer = Guid.NewGuid();
        var assigned = await SeedAsync(colleague, NoteVisibility.Team, NoteTargetType.Customer, assignedCustomer);
        var unassigned = await SeedAsync(colleague, NoteVisibility.Team, NoteTargetType.Customer, Guid.NewGuid());
        var general = await SeedAsync(colleague, NoteVisibility.Team);
        await using (var db = NewDb())
        {
            db.CustomerAssignmentProjections.Add(
                CustomerAssignmentProjection.Create(tenantId, assignedCustomer, viewer, BaseUtc)
            );
            await db.SaveChangesAsync();
        }
        Guid[] ids = [assigned.Id, unassigned.Id, general.Id];

        AssertSameIds([assigned.Id, general.Id], await TitleIdsAsync(ids, hasViewAll: true, assignedTo: viewer));
        AssertSameIds([assigned.Id, unassigned.Id, general.Id], await TitleIdsAsync(ids, hasViewAll: true));
    }

    [Fact]
    public async Task Deleted_missing_and_other_tenant_notes_are_omitted()
    {
        var visible = await SeedAsync(viewer, NoteVisibility.Team, html: "<p>Llamar a Ana</p>");
        var deleted = await SeedAsync(viewer, NoteVisibility.Team, delete: true);
        var foreign = await SeedAsync(viewer, NoteVisibility.Team, tenant: Guid.NewGuid());

        await using var db = NewDb();
        var rows = await new NoteRepository(db).ListTitlesAsync(
            tenantId,
            [visible.Id, deleted.Id, foreign.Id, Guid.NewGuid()],
            viewer,
            true,
            null
        );

        var row = Assert.Single(rows);
        Assert.Equal((visible.Id, "Llamar a Ana"), (row.Id, row.Text));
    }

    // ----- helpers -----

    private static void AssertSameIds(Guid[] expected, List<Guid> actual) =>
        Assert.Equal(expected.Order(), actual.Order());

    private async Task<List<Guid>> TitleIdsAsync(
        IReadOnlyCollection<Guid> ids,
        bool hasViewAll,
        Guid? assignedTo = null
    )
    {
        await using var db = NewDb();
        var rows = await new NoteRepository(db).ListTitlesAsync(tenantId, ids, viewer, hasViewAll, assignedTo);
        return rows.Select(row => row.Id).ToList();
    }

    private async Task<Note> SeedAsync(
        Guid author,
        NoteVisibility visibility,
        NoteTargetType targetType = NoteTargetType.None,
        Guid? targetId = null,
        string html = "<p>nota</p>",
        bool delete = false,
        Guid? tenant = null
    )
    {
        var note = Note.Create(
            tenant ?? tenantId,
            author,
            NoteContent.Create(html).Value,
            NoteReference.Create(targetType, targetId).Value,
            visibility,
            null,
            BaseUtc
        ).Value;
        if (delete)
            Assert.True(note.SoftDelete(author, BaseUtc.AddMinutes(1)).IsSuccess);

        await using var db = NewDb();
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return note;
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
