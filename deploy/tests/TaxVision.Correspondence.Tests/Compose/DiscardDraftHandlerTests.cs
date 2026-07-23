using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class DiscardDraftHandlerTests
{
    [Fact]
    public async Task Handle_WithAnActiveDraft_DiscardsItAndPersists()
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();

        var result = await DiscardDraftHandler.Handle(
            new DiscardDraftCommand(tenantId, draft.Id),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(DraftStatus.Discarded, draft.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    /// <summary>
    /// Chosen posture (see DiscardDraftHandler doc comment): DELETE on an already-Discarded draft
    /// is an idempotent success — same end state the caller already asked for, no domain change to
    /// re-apply, so no extra SaveChanges call either.
    /// </summary>
    [Fact]
    public async Task Handle_CalledTwice_OnTheSameDraft_IsIdempotentAndDoesNotSaveAgain()
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();
        var command = new DiscardDraftCommand(tenantId, draft.Id);

        var first = await DiscardDraftHandler.Handle(command, drafts, unitOfWork, CancellationToken.None);
        var second = await DiscardDraftHandler.Handle(command, drafts, unitOfWork, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(DraftStatus.Discarded, draft.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    /// <summary>
    /// Discarding a Sent/Sending/Failed draft is NOT the same action repeated — it never was valid
    /// for that draft (Draft.Discard's own class-level invariant, Fase 10) — so this propagates the
    /// real 409 instead of pretending it succeeded.
    /// </summary>
    [Theory]
    [InlineData(DraftStatus.Sending)]
    [InlineData(DraftStatus.Sent)]
    [InlineData(DraftStatus.Failed)]
    public async Task Handle_OnADraftThatWasNeverValidToDiscard_PropagatesTheConflict(DraftStatus status)
    {
        var tenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.MarkSending();
        if (status == DraftStatus.Sent)
            draft.MarkSent(Guid.NewGuid());
        if (status == DraftStatus.Failed)
            draft.MarkFailed("provider timeout");
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();

        var result = await DiscardDraftHandler.Handle(
            new DiscardDraftCommand(tenantId, draft.Id),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
        Assert.Equal(status, draft.Status);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithUnknownDraft_ReturnsNotFound()
    {
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await DiscardDraftHandler.Handle(
            new DiscardDraftCommand(Guid.NewGuid(), Guid.NewGuid()),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_ForADraftInAnotherTenant_ReturnsNotFound_AndNeverDiscardsIt()
    {
        var owningTenantId = Guid.NewGuid();
        var callerTenantId = Guid.NewGuid();
        var draft = Draft.CreateNew(owningTenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);
        var unitOfWork = new FakeUnitOfWork();

        var result = await DiscardDraftHandler.Handle(
            new DiscardDraftCommand(callerTenantId, draft.Id),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.NotFound", result.Error.Code);
        Assert.Equal(DraftStatus.Draft, draft.Status);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
