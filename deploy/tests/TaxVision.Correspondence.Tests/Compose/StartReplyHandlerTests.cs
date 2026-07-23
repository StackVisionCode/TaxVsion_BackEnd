using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class StartReplyHandlerTests
{
    private static EmailAddress Address(string value) => EmailAddress.Create(value).Value;

    private static async Task<IncomingEmail> SeedIncomingEmailAsync(
        FakeIncomingEmailRepository incomingEmails,
        Guid tenantId,
        Guid customerId,
        string? internetMessageId = "<original@example.com>",
        string? references = "<a@example.com>,<b@example.com>"
    )
    {
        var email = IncomingEmail
            .Create(
                tenantId,
                customerId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "gmail",
                "provider-msg-original",
                Address("customer@example.com"),
                null,
                "Tax question",
                "Snippet",
                DateTime.UtcNow,
                hasAttachments: false,
                attachmentCount: 0,
                internetMessageId: internetMessageId,
                references: references
            )
            .Value;

        await incomingEmails.AddAsync(email);
        return email;
    }

    [Fact]
    public async Task Handle_WithUnknownIncomingEmail_ReturnsNotFound()
    {
        var incomingEmails = new FakeIncomingEmailRepository();
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await StartReplyHandler.Handle(
            new StartReplyCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            incomingEmails,
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
        Assert.Empty(drafts.All);
    }

    [Fact]
    public async Task Handle_BuildsTheReplyContext_FromTheIncomingEmailsOwnPersistedFields()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var email = await SeedIncomingEmailAsync(incomingEmails, tenantId, customerId);
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();
        var accountId = Guid.NewGuid();

        var result = await StartReplyHandler.Handle(
            new StartReplyCommand(tenantId, email.Id, accountId, Guid.NewGuid()),
            incomingEmails,
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Re: Tax question", result.Value.Subject);

        var replyContext = result.Value.ReplyContext;
        Assert.Equal(email.Id, replyContext.IncomingEmailId);
        Assert.Equal(email.EmailThreadId, replyContext.EmailThreadId);
        Assert.Equal(email.InternetMessageId, replyContext.InReplyToInternetMessageId);
        Assert.Equal(email.ProviderMessageId, replyContext.ReplyToProviderMessageId);
        // The replied-to email's own References chain, PLUS its own InternetMessageId appended at
        // the end — the replied-to message itself now becomes part of the conversation history.
        Assert.Equal(["<a@example.com>", "<b@example.com>", "<original@example.com>"], replyContext.References);

        var persisted = Assert.Single(drafts.All);
        Assert.Equal(result.Value.DraftId, persisted.Id);
        Assert.Equal(accountId, persisted.AccountId);
        Assert.Equal(customerId, persisted.CustomerId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithNoReferencesOnTheIncomingEmail_BuildsReferencesListWithJustTheRepliedMessageId()
    {
        // The replied-to message ("<original@example.com>") has no References of its own — it is
        // the first message in the thread. Replying to it must still produce a single-element
        // References list (its own InternetMessageId), not null/empty — per Fase 13's threading
        // fix, the replied-to message always becomes part of the new reply's References chain.
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var email = await SeedIncomingEmailAsync(incomingEmails, tenantId, customerId, references: null);
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await StartReplyHandler.Handle(
            new StartReplyCommand(tenantId, email.Id, Guid.NewGuid(), Guid.NewGuid()),
            incomingEmails,
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal([email.InternetMessageId!], result.Value.ReplyContext.References);
    }

    [Fact]
    public async Task Handle_ReplyingToAMessageThatWasItselfAReply_AccumulatesTheFullReferencesChain()
    {
        // A (first message, no References) <- B (reply to A: References = [<A-id>]) <- reply to B.
        // ReplyContext.References for "reply to B" must be [<A-id>, <B-id>]: B's own References
        // chain PLUS B's own InternetMessageId appended — not just copied forward from B unchanged.
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var messageB = await SeedIncomingEmailAsync(
            incomingEmails,
            tenantId,
            customerId,
            internetMessageId: "<B-id>",
            references: "<A-id>"
        );
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await StartReplyHandler.Handle(
            new StartReplyCommand(tenantId, messageB.Id, Guid.NewGuid(), Guid.NewGuid()),
            incomingEmails,
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(["<A-id>", "<B-id>"], result.Value.ReplyContext.References);
    }

    [Fact]
    public async Task Handle_WhenTheRepliedMessageHasNoInternetMessageId_FallsBackToForwardingItsReferencesAsIs()
    {
        // Defensive edge case: a persisted IncomingEmail should always have an InternetMessageId,
        // but if it were somehow missing there is nothing to append, so the existing References
        // chain is forwarded unchanged rather than silently dropped.
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var email = await SeedIncomingEmailAsync(
            incomingEmails,
            tenantId,
            customerId,
            internetMessageId: null,
            references: "<a@example.com>,<b@example.com>"
        );
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await StartReplyHandler.Handle(
            new StartReplyCommand(tenantId, email.Id, Guid.NewGuid(), Guid.NewGuid()),
            incomingEmails,
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(["<a@example.com>", "<b@example.com>"], result.Value.ReplyContext.References);
    }

    [Fact]
    public async Task Handle_CalledTwice_ForTheSameOpenReply_ReusesTheExistingDraftInsteadOfDuplicating()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var email = await SeedIncomingEmailAsync(incomingEmails, tenantId, customerId);
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();
        var command = new StartReplyCommand(tenantId, email.Id, Guid.NewGuid(), Guid.NewGuid());

        var firstResult = await StartReplyHandler.Handle(
            command,
            incomingEmails,
            drafts,
            unitOfWork,
            CancellationToken.None
        );
        var secondResult = await StartReplyHandler.Handle(
            command,
            incomingEmails,
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);
        Assert.Equal(firstResult.Value.DraftId, secondResult.Value.DraftId);
        Assert.Single(drafts.All);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_AfterTheExistingDraftWasDiscarded_CreatesANewOneInsteadOfReusingIt()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var incomingEmails = new FakeIncomingEmailRepository();
        var email = await SeedIncomingEmailAsync(incomingEmails, tenantId, customerId);
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();
        var command = new StartReplyCommand(tenantId, email.Id, Guid.NewGuid(), Guid.NewGuid());

        var firstResult = await StartReplyHandler.Handle(
            command,
            incomingEmails,
            drafts,
            unitOfWork,
            CancellationToken.None
        );
        var firstDraft = drafts.All.Single(d => d.Id == firstResult.Value.DraftId);
        firstDraft.Discard();

        var secondResult = await StartReplyHandler.Handle(
            command,
            incomingEmails,
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(secondResult.IsSuccess);
        Assert.NotEqual(firstResult.Value.DraftId, secondResult.Value.DraftId);
        Assert.Equal(2, drafts.All.Count);
    }
}
