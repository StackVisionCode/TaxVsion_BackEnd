using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Notes.Commands;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Tests.Application;

public sealed class ReconcilePendingAttachmentHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Handle_RemoteAvailable_MarksAttachmentAvailable()
    {
        var (repo, note, fileId) = Seed();
        var uow = new NoOpUnitOfWork();

        await HandleAsync(repo, note, fileId, RemoteFileStatus.Available, uow);

        Assert.Equal(NoteAttachmentStatus.Available, note.Attachments.Single().Status);
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task Handle_RemoteScanFailed_MarksRejected()
    {
        var (repo, note, fileId) = Seed();

        await HandleAsync(repo, note, fileId, RemoteFileStatus.ScanFailed, new NoOpUnitOfWork());

        var attachment = note.Attachments.Single();
        Assert.Equal(NoteAttachmentStatus.Rejected, attachment.Status);
        Assert.Equal("scan-failed", attachment.RejectionReason);
    }

    [Fact]
    public async Task Handle_RemoteInfected_MarksRejectedInfected()
    {
        var (repo, note, fileId) = Seed();

        await HandleAsync(repo, note, fileId, RemoteFileStatus.Infected, new NoOpUnitOfWork());

        Assert.Equal("infected", note.Attachments.Single().RejectionReason);
    }

    [Fact]
    public async Task Handle_RemoteNotFound_MarksRejectedFileMissing()
    {
        var (repo, note, fileId) = Seed();

        await HandleAsync(repo, note, fileId, RemoteFileStatus.NotFound, new NoOpUnitOfWork());

        var attachment = note.Attachments.Single();
        Assert.Equal(NoteAttachmentStatus.Rejected, attachment.Status);
        Assert.Equal("file-missing", attachment.RejectionReason);
    }

    [Fact]
    public async Task Handle_RemotePendingReview_MarksUnderReview()
    {
        var (repo, note, fileId) = Seed();

        await HandleAsync(repo, note, fileId, RemoteFileStatus.PendingReview, new NoOpUnitOfWork());

        Assert.Equal(NoteAttachmentStatus.UnderReview, note.Attachments.Single().Status);
    }

    [Theory]
    [InlineData(RemoteFileStatus.Scanning)]
    [InlineData(RemoteFileStatus.Deleted)]
    public async Task Handle_RemoteStillScanning_DoesNothing(RemoteFileStatus status)
    {
        var (repo, note, fileId) = Seed();
        var uow = new NoOpUnitOfWork();

        await HandleAsync(repo, note, fileId, status, uow);

        Assert.Equal(NoteAttachmentStatus.Pending, note.Attachments.Single().Status);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task Handle_AttachmentDetached_DoesNothing()
    {
        var (repo, note, fileId) = Seed();
        Assert.True(note.DetachFile(fileId).IsSuccess);
        var files = new FakeScanStatusClient(RemoteFileStatus.Available);
        var uow = new NoOpUnitOfWork();

        await ReconcilePendingAttachmentHandler.Handle(
            new ReconcilePendingAttachmentCommand(TenantId, note.Id, fileId),
            repo,
            files,
            uow,
            NullLogger<ReconcilePendingAttachmentCommand>.Instance,
            CancellationToken.None
        );

        Assert.Equal(NoteAttachmentStatus.Detached, note.Attachments.Single().Status);
        Assert.Equal(0, files.Calls);
        Assert.Equal(0, uow.SaveCount);
    }

    [Fact]
    public async Task Handle_RemoteError_Throws()
    {
        var (repo, note, fileId) = Seed();
        var uow = new NoOpUnitOfWork();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            ReconcilePendingAttachmentHandler.Handle(
                new ReconcilePendingAttachmentCommand(TenantId, note.Id, fileId),
                repo,
                new FakeScanStatusClient(error: new HttpRequestException("down")),
                uow,
                NullLogger<ReconcilePendingAttachmentCommand>.Instance,
                CancellationToken.None
            )
        );

        Assert.Equal(NoteAttachmentStatus.Pending, note.Attachments.Single().Status);
        Assert.Equal(0, uow.SaveCount);
    }

    private static (FakeNoteRepository Repo, Note Note, Guid FileId) Seed()
    {
        var repo = new FakeNoteRepository();
        var note = Note.Create(
            TenantId,
            Guid.NewGuid(),
            NoteContent.Create("<p>hi</p>").Value,
            NoteReference.Create(NoteTargetType.None, null).Value,
            NoteVisibility.Private,
            null
        ).Value;
        var fileId = Guid.NewGuid();
        Assert.True(note.AttachFile(fileId, "receipt.pdf", "application/pdf", 1024).IsSuccess);
        repo.Seed(note);
        return (repo, note, fileId);
    }

    private static Task HandleAsync(
        FakeNoteRepository repo,
        Note note,
        Guid fileId,
        RemoteFileStatus status,
        NoOpUnitOfWork uow
    ) =>
        ReconcilePendingAttachmentHandler.Handle(
            new ReconcilePendingAttachmentCommand(TenantId, note.Id, fileId),
            repo,
            new FakeScanStatusClient(status),
            uow,
            NullLogger<ReconcilePendingAttachmentCommand>.Instance,
            CancellationToken.None
        );

    private sealed class FakeScanStatusClient(
        RemoteFileStatus status = RemoteFileStatus.Scanning,
        Exception? error = null
    ) : INoteFileScanStatusClient
    {
        public int Calls { get; private set; }

        public Task<RemoteFileStatus> GetStatusAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            Calls++;
            return error is null ? Task.FromResult(status) : Task.FromException<RemoteFileStatus>(error);
        }
    }
}
