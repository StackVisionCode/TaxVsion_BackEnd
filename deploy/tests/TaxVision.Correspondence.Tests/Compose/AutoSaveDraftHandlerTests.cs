using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class AutoSaveDraftHandlerTests
{
    private static async Task<Draft> SeedDraftAsync(FakeDraftRepository drafts, Guid tenantId)
    {
        var draft = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        await drafts.AddAsync(draft);
        return draft;
    }

    [Fact]
    public async Task Handle_ThreeSuccessiveAutoSaves_EachSucceeds_AndFinalStateReflectsPartialUpdates()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = await SeedDraftAsync(drafts, tenantId);
        var unitOfWork = new FakeUnitOfWork();

        var first = await AutoSaveDraftHandler.Handle(
            new AutoSaveDraftCommand(tenantId, draft.Id, "Subject 1", null, null, null, null, null),
            drafts,
            unitOfWork,
            CancellationToken.None
        );
        var second = await AutoSaveDraftHandler.Handle(
            new AutoSaveDraftCommand(
                tenantId,
                draft.Id,
                null,
                "<p>body</p>",
                null,
                [new AutoSaveDraftRecipientInput("a@example.com", "A")],
                null,
                null
            ),
            drafts,
            unitOfWork,
            CancellationToken.None
        );
        var third = await AutoSaveDraftHandler.Handle(
            new AutoSaveDraftCommand(tenantId, draft.Id, "Subject final", null, null, null, null, null),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(third.IsSuccess);

        // Subject was overwritten by the 3rd call, but htmlBody (set by the 2nd call, untouched
        // by the 3rd) and the recipient (also set by the 2nd call) were never cleared — this is
        // the partial-update guarantee: a call that only sends subject doesn't wipe the rest.
        Assert.Equal("Subject final", draft.Subject);
        Assert.Equal("<p>body</p>", draft.HtmlBody);
        var recipient = Assert.Single(draft.Recipients);
        Assert.Equal("a@example.com", recipient.Address);
        Assert.Equal(3, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithOnlyToProvided_ReplacesToButPreservesExistingCcAndBcc()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = await SeedDraftAsync(drafts, tenantId);
        var unitOfWork = new FakeUnitOfWork();

        await AutoSaveDraftHandler.Handle(
            new AutoSaveDraftCommand(
                tenantId,
                draft.Id,
                null,
                null,
                null,
                [new AutoSaveDraftRecipientInput("to1@example.com", null)],
                [new AutoSaveDraftRecipientInput("cc1@example.com", null)],
                [new AutoSaveDraftRecipientInput("bcc1@example.com", null)]
            ),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        var result = await AutoSaveDraftHandler.Handle(
            new AutoSaveDraftCommand(
                tenantId,
                draft.Id,
                null,
                null,
                null,
                [new AutoSaveDraftRecipientInput("to2@example.com", null)],
                null,
                null
            ),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(3, draft.Recipients.Count);
        Assert.Contains(draft.Recipients, r => r.Address == "to2@example.com");
        Assert.DoesNotContain(draft.Recipients, r => r.Address == "to1@example.com");
        Assert.Contains(draft.Recipients, r => r.Address == "cc1@example.com");
        Assert.Contains(draft.Recipients, r => r.Address == "bcc1@example.com");
    }

    [Theory]
    [InlineData(DraftStatus.Sending)]
    [InlineData(DraftStatus.Discarded)]
    public async Task Handle_OnADraftThatIsNoLongerEditable_ReturnsAConflict(DraftStatus status)
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = await SeedDraftAsync(drafts, tenantId);
        if (status == DraftStatus.Discarded)
            draft.Discard();
        else
            draft.MarkSending();
        var unitOfWork = new FakeUnitOfWork();

        var result = await AutoSaveDraftHandler.Handle(
            new AutoSaveDraftCommand(tenantId, draft.Id, "New subject", null, null, null, null, null),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.InvalidTransition", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithUnknownDraft_ReturnsNotFound()
    {
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await AutoSaveDraftHandler.Handle(
            new AutoSaveDraftCommand(Guid.NewGuid(), Guid.NewGuid(), "x", null, null, null, null, null),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithAnInvalidRecipientAddress_FailsAndDoesNotPersist()
    {
        var tenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = await SeedDraftAsync(drafts, tenantId);
        var unitOfWork = new FakeUnitOfWork();

        var result = await AutoSaveDraftHandler.Handle(
            new AutoSaveDraftCommand(
                tenantId,
                draft.Id,
                null,
                null,
                null,
                [new AutoSaveDraftRecipientInput("not-an-email", null)],
                null,
                null
            ),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailAddress.Invalid", result.Error.Code);
        Assert.Empty(draft.Recipients);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_ForADraftInAnotherTenant_ReturnsNotFound_AndNeverAppliesTheChange()
    {
        var owningTenantId = Guid.NewGuid();
        var callerTenantId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = await SeedDraftAsync(drafts, owningTenantId);
        var unitOfWork = new FakeUnitOfWork();

        var result = await AutoSaveDraftHandler.Handle(
            new AutoSaveDraftCommand(callerTenantId, draft.Id, "Hijacked subject", null, null, null, null, null),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.NotFound", result.Error.Code);
        Assert.Equal(string.Empty, draft.Subject);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
