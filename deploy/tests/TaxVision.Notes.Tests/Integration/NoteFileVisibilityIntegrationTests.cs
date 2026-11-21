using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Infrastructure.Persistence;
using TaxVision.Notes.Infrastructure.Persistence.Repositories;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>
/// Adjuntos de notas visibles contra el SQL real: el predicado del feed (privadas del autor o con notes.view_all, sin
/// borradas, asignación de clientes) más el adjunto disponible.
/// </summary>
public sealed class NoteFileVisibilityIntegrationTests
{
    private static readonly DateTime BaseUtc = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid viewer = Guid.NewGuid();
    private readonly Guid colleague = Guid.NewGuid();

    [Fact]
    public async Task A_private_note_of_another_author_needs_view_all()
    {
        var file = Guid.NewGuid();
        await SeedAsync(colleague, NoteVisibility.Private, [file]);

        Assert.Empty(await FileIdsAsync([file], hasViewAll: false));
        Assert.Equal([file], await FileIdsAsync([file], hasViewAll: true));
    }

    [Fact]
    public async Task The_author_sees_the_files_of_his_private_note()
    {
        var file = Guid.NewGuid();
        await SeedAsync(viewer, NoteVisibility.Private, [file]);

        Assert.Equal([file], await FileIdsAsync([file], hasViewAll: false));
    }

    [Fact]
    public async Task A_deleted_note_is_never_listed_not_even_with_view_all()
    {
        var file = Guid.NewGuid();
        await SeedAsync(viewer, NoteVisibility.Team, [file], delete: true);

        Assert.Empty(await FileIdsAsync([file], hasViewAll: true));
    }

    [Fact]
    public async Task An_archived_note_is_listed()
    {
        var file = Guid.NewGuid();
        await SeedAsync(colleague, NoteVisibility.Team, [file], archive: true);

        Assert.Equal([file], await FileIdsAsync([file], hasViewAll: false));
    }

    [Fact]
    public async Task Only_available_attachments_are_listed()
    {
        var available = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var detached = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        await SeedAsync(
            colleague,
            NoteVisibility.Team,
            [available, pending, detached, rejected],
            configure: note =>
            {
                note.MarkAttachmentAvailable(available, BaseUtc);
                note.MarkAttachmentAvailable(detached, BaseUtc);
                note.DetachFile(detached, BaseUtc);
                note.MarkAttachmentRejected(rejected, "infected", BaseUtc);
            },
            markAvailable: false
        );

        Assert.Equal([available], await FileIdsAsync([available, pending, detached, rejected], hasViewAll: true));
    }

    [Fact]
    public async Task A_file_in_a_private_note_of_another_author_and_in_a_team_note_is_visible()
    {
        var file = Guid.NewGuid();
        await SeedAsync(colleague, NoteVisibility.Private, [file]);
        var team = await SeedAsync(colleague, NoteVisibility.Team, [file]);

        await using var db = NewDb();
        var rows = await new NoteRepository(db).ListVisibleAttachmentFilesAsync(tenantId, [file], viewer, false, null);

        Assert.Equal(team.Id, Assert.Single(rows).NoteId);
    }

    [Fact]
    public async Task With_the_assignment_filter_a_note_of_an_unassigned_customer_is_omitted()
    {
        var assignedCustomer = Guid.NewGuid();
        var assignedFile = Guid.NewGuid();
        var unassignedFile = Guid.NewGuid();
        var generalFile = Guid.NewGuid();
        await SeedAsync(colleague, NoteVisibility.Team, [assignedFile], NoteTargetType.Customer, assignedCustomer);
        await SeedAsync(colleague, NoteVisibility.Team, [unassignedFile], NoteTargetType.Customer, Guid.NewGuid());
        await SeedAsync(colleague, NoteVisibility.Team, [generalFile]);
        await using (var db = NewDb())
        {
            db.CustomerAssignmentProjections.Add(
                CustomerAssignmentProjection.Create(tenantId, assignedCustomer, viewer, BaseUtc)
            );
            await db.SaveChangesAsync();
        }
        Guid[] files = [assignedFile, unassignedFile, generalFile];

        Assert.Equal(
            new[] { assignedFile, generalFile }.Order(),
            (await FileIdsAsync(files, hasViewAll: true, assignedTo: viewer)).Order()
        );
        Assert.Equal(files.Order(), (await FileIdsAsync(files, hasViewAll: true)).Order());
    }

    [Fact]
    public async Task Another_tenant_is_never_listed_and_the_row_carries_the_note_data()
    {
        var file = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var note = await SeedAsync(
            viewer,
            NoteVisibility.Private,
            [file],
            NoteTargetType.Customer,
            customer,
            html: "<p>Llamar a Ana</p>"
        );
        await SeedAsync(viewer, NoteVisibility.Team, [file], tenant: Guid.NewGuid());

        await using var db = NewDb();
        var row = Assert.Single(
            await new NoteRepository(db).ListVisibleAttachmentFilesAsync(tenantId, [file], viewer, false, null)
        );

        Assert.Equal(
            (file, note.Id, "Llamar a Ana", NoteVisibility.Private, viewer, NoteTargetType.Customer, (Guid?)customer),
            (row.FileId, row.NoteId, row.Text, row.Visibility, row.AuthorUserId, row.TargetType, row.TargetId)
        );
    }

    // ----- helpers -----

    private async Task<List<Guid>> FileIdsAsync(
        IReadOnlyCollection<Guid> files,
        bool hasViewAll,
        Guid? assignedTo = null
    )
    {
        await using var db = NewDb();
        var rows = await new NoteRepository(db).ListVisibleAttachmentFilesAsync(
            tenantId,
            files,
            viewer,
            hasViewAll,
            assignedTo
        );
        return rows.Select(row => row.FileId).ToList();
    }

    private async Task<Note> SeedAsync(
        Guid author,
        NoteVisibility visibility,
        Guid[] files,
        NoteTargetType targetType = NoteTargetType.None,
        Guid? targetId = null,
        string html = "<p>nota</p>",
        bool delete = false,
        bool archive = false,
        Guid? tenant = null,
        Action<Note>? configure = null,
        bool markAvailable = true
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
        foreach (var file in files)
        {
            Assert.True(note.AttachFile(file, "a.pdf", "application/pdf", 10, BaseUtc).IsSuccess);
            if (markAvailable)
                note.MarkAttachmentAvailable(file, BaseUtc);
        }
        configure?.Invoke(note);
        if (archive)
            Assert.True(note.Archive(author, BaseUtc.AddMinutes(1)).IsSuccess);
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
