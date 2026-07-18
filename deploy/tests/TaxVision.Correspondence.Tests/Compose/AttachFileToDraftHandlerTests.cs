using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Tests.Messages;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class AttachFileToDraftHandlerTests
{
    [Fact]
    public async Task Handle_ThreeDifferentFiles_AreAllPersistedOnTheDraft()
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid()).Value;
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();
        var cloudStorage = new FakeCloudStorageClient();

        foreach (var i in Enumerable.Range(1, 3))
        {
            var result = await AttachFileToDraftHandler.Handle(
                new AttachFileToDraftCommand(
                    tenantId,
                    draft.Id,
                    Guid.NewGuid(),
                    $"file{i}.pdf",
                    "application/pdf",
                    100 * i
                ),
                drafts,
                cloudStorage,
                unitOfWork,
                NullLogger<AttachFileToDraftCommand>.Instance,
                CancellationToken.None
            );
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(3, draft.Attachments.Count);
        Assert.Equal(3, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_SameFileIdTwice_DoesNotCreateADuplicate()
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid()).Value;
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();
        var cloudStorage = new FakeCloudStorageClient();
        var fileId = Guid.NewGuid();
        var command = new AttachFileToDraftCommand(tenantId, draft.Id, fileId, "invoice.pdf", "application/pdf", 1024);

        var first = await AttachFileToDraftHandler.Handle(
            command,
            drafts,
            cloudStorage,
            unitOfWork,
            NullLogger<AttachFileToDraftCommand>.Instance,
            CancellationToken.None
        );
        var second = await AttachFileToDraftHandler.Handle(
            command,
            drafts,
            cloudStorage,
            unitOfWork,
            NullLogger<AttachFileToDraftCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Single(draft.Attachments);
    }

    [Theory]
    [InlineData(DraftStatus.Sending)]
    [InlineData(DraftStatus.Sent)]
    [InlineData(DraftStatus.Discarded)]
    public async Task Handle_OnADraftNotInDraftStatus_ReturnsInvalidTransition(DraftStatus status)
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid()).Value;
        MoveTo(draft, status);
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();
        var cloudStorage = new FakeCloudStorageClient();

        var result = await AttachFileToDraftHandler.Handle(
            new AttachFileToDraftCommand(tenantId, draft.Id, Guid.NewGuid(), "invoice.pdf", "application/pdf", 1024),
            drafts,
            cloudStorage,
            unitOfWork,
            NullLogger<AttachFileToDraftCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    /// <summary>
    /// The CloudStorage verification call (<see cref="ICloudStorageClient.GetFileMetadataAsync"/>)
    /// is best-effort: a failure there (404, timeout, missing M2M grant) is logged and the attach
    /// proceeds anyway — see AttachFileToDraftHandler's class doc comment for the rationale.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCloudStorageVerificationFails_StillAttachesTheFile()
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid()).Value;
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();
        var cloudStorage = new FakeCloudStorageClient
        {
            MetadataResponse = Result.Failure<CloudStorageFileMetadata>(
                new Error("CloudStorageClient.RequestFailed", "timed out")
            ),
        };
        var fileId = Guid.NewGuid();

        var result = await AttachFileToDraftHandler.Handle(
            new AttachFileToDraftCommand(tenantId, draft.Id, fileId, "invoice.pdf", "application/pdf", 1024),
            drafts,
            cloudStorage,
            unitOfWork,
            NullLogger<AttachFileToDraftCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(draft.Attachments);
        Assert.Equal(fileId, draft.Attachments.Single().FileId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithUnknownDraft_ReturnsNotFound()
    {
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();
        var cloudStorage = new FakeCloudStorageClient();

        var result = await AttachFileToDraftHandler.Handle(
            new AttachFileToDraftCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "invoice.pdf",
                "application/pdf",
                1024
            ),
            drafts,
            cloudStorage,
            unitOfWork,
            NullLogger<AttachFileToDraftCommand>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.NotFound", result.Error.Code);
    }

    private static void MoveTo(Draft draft, DraftStatus status)
    {
        switch (status)
        {
            case DraftStatus.Draft:
                return;
            case DraftStatus.Discarded:
                draft.Discard();
                return;
            case DraftStatus.Sending:
                draft.MarkSending();
                return;
            case DraftStatus.Sent:
                draft.MarkSending();
                draft.MarkSent(Guid.NewGuid());
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }
}
