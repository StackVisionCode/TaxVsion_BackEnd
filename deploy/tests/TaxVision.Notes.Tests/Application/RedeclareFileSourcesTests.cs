using System.Reflection;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;
using TaxVision.Notes.Infrastructure.Notes;
using TaxVision.Notes.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Notes.Tests.Application;

/// <summary>
/// Re-declaración de los adjuntos que Notes ya tenía: los de notas no borradas, sin retirados ni rechazados. Con dryRun
/// solo cuenta; en real publica por páginas, cada una con su guardado.
/// </summary>
public sealed class RedeclareFileSourcesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Dry_run_counts_without_publishing()
    {
        var (db, expected) = await SeedAsync();
        var (outbox, recorder) = RecordingOutbox.Create(db);

        var report = await new NoteFileSourceRedeclarer(outbox, new NoOpCorrelationContext()).RedeclareAsync(
            TenantId,
            true,
            default
        );

        Assert.Equal(new RedeclareFileSourcesReport(true, 3, expected.Count), report);
        Assert.Empty(recorder.Pending);
        Assert.Empty(recorder.Flushed);
        Assert.Equal(0, recorder.Saves);
    }

    [Fact]
    public async Task Real_run_declares_each_live_attachment_page_by_page()
    {
        var (db, expected) = await SeedAsync();
        var (outbox, recorder) = RecordingOutbox.Create(db);
        var redeclarer = new NoteFileSourceRedeclarer(outbox, new NoOpCorrelationContext()) { PageSize = 2 };

        var report = await redeclarer.RedeclareAsync(TenantId, false, default);

        Assert.Equal(new RedeclareFileSourcesReport(false, 3, expected.Count), report);
        Assert.Empty(recorder.Pending);
        Assert.Equal(expected.Order(), Keys(recorder.Flushed).Order());
        Assert.Equal(2, recorder.Saves);
        Assert.All(
            recorder.Flushed.Cast<FileSourceDeclaredIntegrationEvent>(),
            e => Assert.Equal((TenantId, "notes"), (e.TenantId, e.SourceService))
        );
    }

    [Fact]
    public async Task Repeating_it_declares_the_same()
    {
        var (db, _) = await SeedAsync();
        var (outbox, recorder) = RecordingOutbox.Create(db);
        var redeclarer = new NoteFileSourceRedeclarer(outbox, new NoOpCorrelationContext());

        await redeclarer.RedeclareAsync(TenantId, false, default);
        var first = Keys(recorder.Flushed).Order().ToArray();
        recorder.Flushed.Clear();
        await redeclarer.RedeclareAsync(TenantId, false, default);

        Assert.Equal(first, Keys(recorder.Flushed).Order());
    }

    private static IEnumerable<string> Keys(IEnumerable<object> messages) =>
        messages
            .Cast<FileSourceDeclaredIntegrationEvent>()
            .Select(e => Key(e.SourceResourceId, e.FileId, e.SourceRole, e.RelatedCustomerId));

    private static string Key(Guid noteId, Guid fileId, string role, Guid? customerId) =>
        $"note|{noteId}|{fileId}|{role}|{customerId}";

    private static async Task<(NotesDbContext Db, List<string> Expected)> SeedAsync()
    {
        var db = new NotesDbContext(
            new DbContextOptionsBuilder<NotesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );
        var expected = new List<string>();
        var customer = Guid.NewGuid();

        var onCustomer = NewNote(TenantId, NoteReference.Create(NoteTargetType.Customer, customer).Value);
        var pending = Attach(onCustomer);
        var available = Attach(onCustomer);
        onCustomer.MarkAttachmentAvailable(available, Now);
        var detached = Attach(onCustomer);
        onCustomer.DetachFile(detached, Now);
        var rejected = Attach(onCustomer);
        onCustomer.MarkAttachmentRejected(rejected, "infected", Now);
        expected.Add(Key(onCustomer.Id, pending, "attachment", customer));
        expected.Add(Key(onCustomer.Id, available, "attachment", customer));

        var archived = NewNote(TenantId, NoteReference.Create(NoteTargetType.None, null).Value);
        var archivedFile = Attach(archived);
        Assert.True(archived.Archive(Author, Now).IsSuccess);
        expected.Add(Key(archived.Id, archivedFile, "attachment", null));

        var onTask = NewNote(TenantId, NoteReference.Create(NoteTargetType.Task, Guid.NewGuid()).Value);
        var taskFile = Attach(onTask);
        expected.Add(Key(onTask.Id, taskFile, "attachment", null));

        var deleted = NewNote(TenantId, NoteReference.Create(NoteTargetType.Customer, customer).Value);
        Attach(deleted);
        Assert.True(deleted.SoftDelete(Author, Now).IsSuccess);

        var foreign = NewNote(Guid.NewGuid(), NoteReference.Create(NoteTargetType.None, null).Value);
        Attach(foreign);

        db.Notes.AddRange(onCustomer, archived, onTask, deleted, foreign);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (db, expected);
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

    /// <summary>Outbox de EF en memoria: lo publicado queda pendiente y solo sale si el guardado confirma.</summary>
    public class RecordingOutbox : DispatchProxy
    {
        internal NotesDbContext? Context { get; private set; }
        internal List<object> Pending { get; } = [];
        internal List<object> Flushed { get; } = [];
        internal int Saves { get; private set; }

        internal static (IDbContextOutbox<NotesDbContext> Outbox, RecordingOutbox Recorder) Create(NotesDbContext db)
        {
            var outbox = Create<IDbContextOutbox<NotesDbContext>, RecordingOutbox>();
            var recorder = (RecordingOutbox)(object)outbox;
            recorder.Context = db;
            return (outbox, recorder);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name switch
            {
                "get_DbContext" => Context,
                "PublishAsync" => Enqueue(args![0]!),
                "SaveChangesAndFlushMessagesAsync" => SaveAndFlushAsync(),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };

        private ValueTask Enqueue(object message)
        {
            Pending.Add(message);
            return ValueTask.CompletedTask;
        }

        private async Task SaveAndFlushAsync()
        {
            await Context!.SaveChangesAsync();
            Saves++;
            Flushed.AddRange(Pending);
            Pending.Clear();
        }
    }
}
