using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notes.Application.Notes.Commands;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

/// <summary>Adjuntar un archivo a una nota lo declara a CloudStorage después de guardar; si falla, no sale nada.</summary>
public sealed class AttachFileDeclaresSourceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();

    [Fact]
    public async Task A_note_on_a_customer_declares_the_note_and_the_customer_after_saving()
    {
        var customerId = Guid.NewGuid();
        var note = NewNote(NoteReference.Create(NoteTargetType.Customer, customerId).Value);
        var fileId = Guid.NewGuid();
        var bus = new FakeMessageBus();
        var unitOfWork = new SnapshotUnitOfWork(bus);

        var result = await AttachAsync(note, fileId, bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.PublishedAtSave);
        var declared = Assert.Single(bus.Published.OfType<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(
            (TenantId, fileId, "notes", "note", note.Id, "attachment", (Guid?)customerId, "test"),
            (
                declared.TenantId,
                declared.FileId,
                declared.SourceService,
                declared.SourceResourceType,
                declared.SourceResourceId,
                declared.SourceRole,
                declared.RelatedCustomerId,
                declared.CorrelationId
            )
        );
    }

    [Theory]
    [InlineData(NoteTargetType.None)]
    [InlineData(NoteTargetType.Task)]
    public async Task A_note_without_a_customer_declares_no_related_customer(NoteTargetType targetType)
    {
        var reference = NoteReference
            .Create(targetType, targetType == NoteTargetType.None ? null : Guid.NewGuid())
            .Value;
        var bus = new FakeMessageBus();

        await AttachAsync(NewNote(reference), Guid.NewGuid(), bus, new SnapshotUnitOfWork(bus));

        Assert.Null(Assert.Single(bus.Published.OfType<FileSourceDeclaredIntegrationEvent>()).RelatedCustomerId);
    }

    [Fact]
    public async Task A_failed_attach_declares_nothing()
    {
        var note = NewNote(NoteReference.Create(NoteTargetType.None, null).Value);
        var fileId = Guid.NewGuid();
        Assert.True(note.AttachFile(fileId, "a.pdf", "application/pdf", 10, DateTime.UtcNow).IsSuccess);
        var bus = new FakeMessageBus();
        var unitOfWork = new SnapshotUnitOfWork(bus);

        var result = await AttachAsync(note, fileId, bus, unitOfWork);

        Assert.True(result.IsFailure);
        Assert.Empty(bus.Published);
        Assert.Equal(-1, unitOfWork.PublishedAtSave);
    }

    private static Task<BuildingBlocks.Results.Result<TaxVision.Notes.Application.Notes.NoteResponse>> AttachAsync(
        Note note,
        Guid fileId,
        FakeMessageBus bus,
        IUnitOfWork unitOfWork
    )
    {
        var repo = new FakeNoteRepository();
        repo.Seed(note);
        return AttachFileToNoteHandler.Handle(
            new AttachFileToNoteCommand(
                TenantId,
                note.Id,
                Author,
                fileId,
                "receipt.pdf",
                "application/pdf",
                1024,
                false
            ),
            TestNoteReaders.For(repo),
            new FakeOffboardedStaffRepository(),
            unitOfWork,
            bus,
            new NoOpCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );
    }

    private static Note NewNote(NoteReference reference) =>
        Note.Create(
            TenantId,
            Author,
            NoteContent.Create("<p>hi</p>").Value,
            reference,
            NoteVisibility.Team,
            null,
            DateTime.UtcNow
        ).Value;

    // Anota cuántos mensajes había publicados en el momento de guardar.
    private sealed class SnapshotUnitOfWork(FakeMessageBus bus) : IUnitOfWork
    {
        public int PublishedAtSave { get; private set; } = -1;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            PublishedAtSave = bus.Published.Count;
            return Task.FromResult(0);
        }
    }
}
