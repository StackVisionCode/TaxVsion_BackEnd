using TaxVision.Notes.Application.Notes.Commands;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

public sealed class AttachFileToNoteHandlerTests
{
    [Fact]
    public async Task Handle_Success_SchedulesReconciliation()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        var note = Note.Create(
            tenantId,
            authorId,
            NoteContent.Create("<p>hi</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            NoteVisibility.Private,
            null,
            DateTime.UtcNow
        ).Value;
        repo.Seed(note);
        var fileId = Guid.NewGuid();
        var uow = new NoOpUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await AttachFileToNoteHandler.Handle(
            new AttachFileToNoteCommand(tenantId, note.Id, authorId, fileId, "receipt.pdf", "application/pdf", 1024, false),
            repo,
            new FakeOffboardedStaffRepository(),
            uow,
            bus,
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, uow.SaveCount);
        var (message, options) = Assert.Single(bus.PublishedWithOptions);
        var scheduled = Assert.IsType<ReconcilePendingAttachmentCommand>(message);
        Assert.Equal(new ReconcilePendingAttachmentCommand(tenantId, note.Id, fileId), scheduled);
        Assert.Equal(TimeSpan.FromSeconds(30), options?.ScheduleDelay);
    }

    [Fact]
    public async Task Handle_StampsTheAttachmentWithTheClock()
    {
        var tenantId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var repo = new FakeNoteRepository();
        var note = Note.Create(
            tenantId,
            authorId,
            NoteContent.Create("<p>hi</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            NoteVisibility.Private,
            null,
            DateTime.UtcNow
        ).Value;
        repo.Seed(note);
        var nowUtc = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

        var result = await AttachFileToNoteHandler.Handle(
            new AttachFileToNoteCommand(tenantId, note.Id, authorId, Guid.NewGuid(), "receipt.pdf", "application/pdf", 1024, false),
            repo,
            new FakeOffboardedStaffRepository(),
            new NoOpUnitOfWork(),
            new FakeMessageBus(),
            new FixedClock(nowUtc),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(nowUtc, note.UpdatedAtUtc);
        Assert.Equal(nowUtc, Assert.Single(note.Attachments).LinkedAtUtc);
    }

    [Fact]
    public async Task Handle_Failure_SchedulesNothing()
    {
        var bus = new FakeMessageBus();

        var result = await AttachFileToNoteHandler.Handle(
            new AttachFileToNoteCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "receipt.pdf",
                "application/pdf",
                1024,
                false
            ),
            new FakeNoteRepository(),
            new FakeOffboardedStaffRepository(),
            new NoOpUnitOfWork(),
            bus,
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Empty(bus.Published);
    }

    private sealed class FixedClock(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(nowUtc);
    }
}
