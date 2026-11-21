using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Application.Notes.Consumers;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Infrastructure.Notes;
using TaxVision.Notes.Infrastructure.Persistence;

namespace TaxVision.Notes.Tests.Application;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen: Notes declara solo los adjuntos suyos de esa oficina, con
/// los mismos valores que la reposición manual, e ignora el resto.
/// </summary>
public sealed class NoteFileSourcesClaimConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Declares_the_claimed_attachments_with_the_same_values_as_the_reconciliation()
    {
        var seeded = await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(seeded.Db, bus, TenantId, seeded.AllFiles);

        Assert.Equal((await ReconciledAsync(seeded.Db)).Order(), Keys(bus.Published).Order());
        Assert.Equal(seeded.Declarable.Order(), Keys(bus.Published).Order());
    }

    [Fact]
    public async Task Declares_only_the_claimed_ids_and_ignores_foreign_ones()
    {
        var seeded = await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(seeded.Db, bus, TenantId, [seeded.Pending, seeded.Detached, seeded.Foreign, Guid.NewGuid()]);

        var declared = Assert.Single(bus.Published.Cast<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(
            (TenantId, seeded.Pending, "notes", "note", seeded.OnCustomer, "attachment", (Guid?)Customer),
            (
                declared.TenantId,
                declared.FileId,
                declared.SourceService,
                declared.SourceResourceType,
                declared.SourceResourceId,
                declared.SourceRole,
                declared.RelatedCustomerId
            )
        );
    }

    [Fact]
    public async Task Another_office_file_is_declared_only_for_its_own_tenant()
    {
        var seeded = await SeedAsync();
        var ours = new FakeMessageBus();
        var theirs = new FakeMessageBus();

        await HandleAsync(seeded.Db, ours, TenantId, [seeded.Foreign]);
        await HandleAsync(seeded.Db, theirs, OtherTenant, [seeded.Foreign]);

        Assert.Empty(ours.Published);
        var declared = Assert.Single(theirs.Published.Cast<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal((OtherTenant, seeded.Foreign), (declared.TenantId, declared.FileId));
    }

    [Fact]
    public async Task Without_matches_nothing_is_published()
    {
        var seeded = await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(seeded.Db, bus, TenantId, [seeded.Rejected, seeded.OnDeleted, Guid.NewGuid()]);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task An_empty_list_or_an_empty_tenant_does_nothing()
    {
        var seeded = await SeedAsync();
        var bus = new FakeMessageBus();

        await HandleAsync(seeded.Db, bus, TenantId, []);
        await HandleAsync(seeded.Db, bus, Guid.Empty, seeded.AllFiles);

        Assert.Empty(bus.Published);
    }

    private static Task HandleAsync(NotesDbContext db, FakeMessageBus bus, Guid tenantId, IReadOnlyList<Guid> fileIds)
    {
        var (outbox, _) = RedeclareFileSourcesTests.RecordingOutbox.Create(db);
        return NoteFileSourcesClaimConsumer.Handle(
            new FileSourcesClaimRequestedIntegrationEvent { TenantId = tenantId, FileIds = fileIds },
            new NoteFileSourceRedeclarer(outbox, new NoOpCorrelationContext()),
            bus,
            new NoOpCorrelationContext(),
            default
        );
    }

    // Lo que publica la reposición manual de la oficina.
    private static async Task<List<string>> ReconciledAsync(NotesDbContext db)
    {
        var (outbox, recorder) = RedeclareFileSourcesTests.RecordingOutbox.Create(db);
        await new NoteFileSourceRedeclarer(outbox, new NoOpCorrelationContext()).RedeclareAsync(
            TenantId,
            false,
            default
        );
        return Keys(recorder.Flushed).ToList();
    }

    private static IEnumerable<string> Keys(IEnumerable<object> messages) =>
        messages
            .Cast<FileSourceDeclaredIntegrationEvent>()
            .Select(e =>
                Key(e.TenantId, e.SourceService, e.SourceResourceType, e.SourceResourceId, e.FileId, e.SourceRole)
                + $"|{e.RelatedCustomerId}"
            );

    private static string Key(Guid tenant, string service, string type, Guid noteId, Guid fileId, string role) =>
        $"{tenant}|{service}|{type}|{noteId}|{fileId}|{role}";

    private static string Expected(Guid noteId, Guid fileId, Guid? customerId) =>
        Key(TenantId, "notes", "note", noteId, fileId, "attachment") + $"|{customerId}";

    private sealed record Seeded(
        NotesDbContext Db,
        Guid OnCustomer,
        Guid Pending,
        Guid Detached,
        Guid Rejected,
        Guid OnDeleted,
        Guid Foreign,
        IReadOnlyList<Guid> AllFiles,
        IReadOnlyList<string> Declarable
    );

    private static async Task<Seeded> SeedAsync()
    {
        var db = new NotesDbContext(
            new DbContextOptionsBuilder<NotesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

        var onCustomer = NewNote(TenantId, NoteReference.Create(NoteTargetType.Customer, Customer).Value);
        var pending = Attach(onCustomer);
        var available = Attach(onCustomer);
        onCustomer.MarkAttachmentAvailable(available, Now);
        var detached = Attach(onCustomer);
        onCustomer.DetachFile(detached, Now);
        var rejected = Attach(onCustomer);
        onCustomer.MarkAttachmentRejected(rejected, "infected", Now);

        var archived = NewNote(TenantId, NoteReference.Create(NoteTargetType.None, null).Value);
        var archivedFile = Attach(archived);
        Assert.True(archived.Archive(Author, Now).IsSuccess);

        // El mismo archivo en una nota de tarea: una declaración por nota, como la reposición.
        var onTask = NewNote(TenantId, NoteReference.Create(NoteTargetType.Task, Guid.NewGuid()).Value);
        Assert.True(onTask.AttachFile(available, "a.pdf", "application/pdf", 10, Now).IsSuccess);

        var deleted = NewNote(TenantId, NoteReference.Create(NoteTargetType.Customer, Customer).Value);
        var onDeleted = Attach(deleted);
        Assert.True(deleted.SoftDelete(Author, Now).IsSuccess);

        var foreignNote = NewNote(OtherTenant, NoteReference.Create(NoteTargetType.None, null).Value);
        var foreign = Attach(foreignNote);

        db.Notes.AddRange(onCustomer, archived, onTask, deleted, foreignNote);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new Seeded(
            db,
            onCustomer.Id,
            pending,
            detached,
            rejected,
            onDeleted,
            foreign,
            [pending, available, detached, rejected, archivedFile, onDeleted, foreign],
            [
                Expected(onCustomer.Id, pending, Customer),
                Expected(onCustomer.Id, available, Customer),
                Expected(archived.Id, archivedFile, null),
                Expected(onTask.Id, available, null),
            ]
        );
    }

    private static Guid Attach(Note note)
    {
        var fileId = Guid.NewGuid();
        Assert.True(note.AttachFile(fileId, "a.pdf", "application/pdf", 10, Now).IsSuccess);
        return fileId;
    }

    private static Note NewNote(Guid tenantId, NoteReference reference) =>
        Note.Create(
            tenantId,
            Author,
            NoteContent.Create("<p>nota</p>").Value,
            reference,
            NoteVisibility.Team,
            null,
            Now
        ).Value;

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
