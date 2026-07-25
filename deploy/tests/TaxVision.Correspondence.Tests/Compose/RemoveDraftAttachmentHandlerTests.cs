using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class RemoveDraftAttachmentHandlerTests
{
    [Fact]
    public async Task Handle_RemovingOneOfThreeAttachments_LeavesTheOtherTwo()
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        var fileIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var fileId in fileIds)
            draft.AttachFile(DraftAttachmentRef.Create(fileId, "file.pdf", "application/pdf", 1024).Value);
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();

        var result = await RemoveDraftAttachmentHandler.Handle(
            new RemoveDraftAttachmentCommand(tenantId, draft.Id, fileIds[1]),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, draft.Attachments.Count);
        Assert.Contains(draft.Attachments, a => a.FileId == fileIds[0]);
        Assert.Contains(draft.Attachments, a => a.FileId == fileIds[2]);
        Assert.DoesNotContain(draft.Attachments, a => a.FileId == fileIds[1]);
    }

    [Fact]
    public async Task Handle_RemovingAFileIdThatWasNeverAttached_SucceedsAsANoOp()
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AttachFile(DraftAttachmentRef.Create(Guid.NewGuid(), "file.pdf", "application/pdf", 1024).Value);
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();

        var result = await RemoveDraftAttachmentHandler.Handle(
            new RemoveDraftAttachmentCommand(tenantId, draft.Id, Guid.NewGuid()),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(draft.Attachments);
    }

    [Theory]
    [InlineData(DraftStatus.Sending)]
    [InlineData(DraftStatus.Sent)]
    public async Task Handle_OnADraftNotInDraftStatus_ReturnsInvalidTransition(DraftStatus status)
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        var fileId = Guid.NewGuid();
        draft.AttachFile(DraftAttachmentRef.Create(fileId, "file.pdf", "application/pdf", 1024).Value);
        draft.MarkSending();
        if (status == DraftStatus.Sent)
            draft.MarkSent(Guid.NewGuid());
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();

        var result = await RemoveDraftAttachmentHandler.Handle(
            new RemoveDraftAttachmentCommand(tenantId, draft.Id, fileId),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
        Assert.Single(draft.Attachments);
    }

    [Fact]
    public async Task Handle_WithUnknownDraft_ReturnsNotFound()
    {
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await RemoveDraftAttachmentHandler.Handle(
            new RemoveDraftAttachmentCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.NotFound", result.Error.Code);
    }
}
