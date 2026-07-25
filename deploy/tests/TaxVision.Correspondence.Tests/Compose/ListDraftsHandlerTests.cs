using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class ListDraftsHandlerTests
{
    [Fact]
    public async Task Handle_WithMixOfStatuses_ReturnsOnlyOpenDraftsOrderedByUpdatedAtUtcDescending()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();

        // Draft has no injectable clock (UpdatedAtUtc comes from the real DateTime.UtcNow), so a
        // small delay guarantees a strict ordering between the two — avoids flakiness from two
        // back-to-back real-clock reads landing on the same tick.
        var older = Draft.CreateNew(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        older.AutoSave("Older draft", null, null, null);
        await drafts.AddAsync(older);

        await Task.Delay(20);

        var newer = Draft.CreateNew(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        newer.AutoSave("Newer draft", null, null, null);
        await drafts.AddAsync(newer);

        var sent = Draft.CreateNew(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        sent.MarkSending();
        sent.MarkSent(Guid.NewGuid());
        await drafts.AddAsync(sent);

        var discarded = Draft.CreateNew(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        discarded.Discard();
        await drafts.AddAsync(discarded);

        var result = await ListDraftsHandler.Handle(
            new ListDraftsQuery(tenantId, customerId, 1, 20),
            drafts,
            CancellationToken.None
        );

        Assert.Equal(2, result.TotalCount);
        Assert.Equal([newer.Id, older.Id], result.Items.Select(x => x.DraftId));
        Assert.All(result.Items, x => Assert.Equal("Draft", x.Status));
    }

    [Fact]
    public async Task Handle_WithPagination_RespectsPageAndSize()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();

        for (var i = 0; i < 5; i++)
        {
            var draft = Draft.CreateNew(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
            draft.AutoSave($"Draft {i}", null, null, null);
            await drafts.AddAsync(draft);
        }

        var page1 = await ListDraftsHandler.Handle(
            new ListDraftsQuery(tenantId, customerId, 1, 2),
            drafts,
            CancellationToken.None
        );
        var page2 = await ListDraftsHandler.Handle(
            new ListDraftsQuery(tenantId, customerId, 2, 2),
            drafts,
            CancellationToken.None
        );
        var page3 = await ListDraftsHandler.Handle(
            new ListDraftsQuery(tenantId, customerId, 3, 2),
            drafts,
            CancellationToken.None
        );

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);
    }

    [Fact]
    public async Task Handle_ReturnsIsReplyTrue_OnlyForDraftsWithReplyContext()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();

        var newDraft = Draft.CreateNew(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        newDraft.AutoSave("New correspondence", null, null, null);
        await drafts.AddAsync(newDraft);

        var replyContext = ReplyContext.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, null).Value;
        var replyDraft = Draft
            .CreateReply(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid(), replyContext, "Original")
            .Value;
        await drafts.AddAsync(replyDraft);

        var result = await ListDraftsHandler.Handle(
            new ListDraftsQuery(tenantId, customerId, 1, 20),
            drafts,
            CancellationToken.None
        );

        Assert.Equal(2, result.TotalCount);
        Assert.False(result.Items.Single(x => x.DraftId == newDraft.Id).IsReply);
        Assert.True(result.Items.Single(x => x.DraftId == replyDraft.Id).IsReply);
    }

    [Fact]
    public async Task Handle_WithDraftsFromAnotherCustomerOrTenant_NeverLeaksThem()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();

        var mine = Draft.CreateNew(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        mine.AutoSave("Mine", null, null, null);
        await drafts.AddAsync(mine);

        var otherCustomer = Draft.CreateNew(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;
        otherCustomer.AutoSave("Other customer", null, null, null);
        await drafts.AddAsync(otherCustomer);

        var otherTenant = Draft.CreateNew(Guid.NewGuid(), customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        otherTenant.AutoSave("Other tenant", null, null, null);
        await drafts.AddAsync(otherTenant);

        var result = await ListDraftsHandler.Handle(
            new ListDraftsQuery(tenantId, customerId, 1, 20),
            drafts,
            CancellationToken.None
        );

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(mine.Id, result.Items.Single().DraftId);
    }
}
