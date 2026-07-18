using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Ingest;

namespace TaxVision.Correspondence.Tests.Threads;

public sealed class ListThreadMessagesHandlerTests
{
    private static EmailAddress Address(string value) => EmailAddress.Create(value).Value;

    private static IncomingEmail NewIncomingEmail(
        Guid tenantId,
        Guid customerId,
        Guid emailThreadId,
        DateTime receivedAtUtc
    ) =>
        IncomingEmail
            .Create(
                tenantId,
                customerId,
                emailThreadId,
                Guid.NewGuid(),
                "gmail",
                $"provider-msg-{Guid.NewGuid()}",
                Address("customer@example.com"),
                "The Customer",
                "Subject",
                "Snippet",
                receivedAtUtc,
                hasAttachments: false,
                attachmentCount: 0
            )
            .Value;

    private static Draft NewSentReplyDraft(Guid tenantId, Guid customerId, Guid emailThreadId, string toAddress)
    {
        var replyContext = ReplyContext.Create(Guid.NewGuid(), emailThreadId, null, null, null).Value;
        var draft = Draft.CreateReply(tenantId, customerId, Guid.NewGuid(), replyContext, "Original subject").Value;
        draft.AutoSave(
            "Re: Original subject",
            "<p>Reply body</p>",
            "Reply body",
            [new DraftRecipientData(Address(toAddress), EmailRecipientType.To, null)]
        );
        draft.MarkSending();
        draft.MarkSent(Guid.NewGuid());
        return draft;
    }

    [Fact]
    public async Task Handle_WithFiveMessages_ReturnsThemInChronologicalOrder()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);

        var now = DateTime.UtcNow;
        var incomingEmails = new FakeIncomingEmailRepository();
        var expectedOrder = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var email = NewIncomingEmail(tenantId, customerId, thread.Id, now.AddMinutes(i));
            expectedOrder.Add(email.Id);
            await incomingEmails.AddAsync(email);
        }

        var drafts = new FakeDraftRepository();

        var result = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(tenantId, thread.Id, 1, 20),
            emailThreads,
            incomingEmails,
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(expectedOrder, result.Value.Items.Select(x => x.MessageId));
        Assert.All(result.Value.Items, x => Assert.Equal(MessageDirection.Inbound, x.Direction));
    }

    [Fact]
    public async Task Handle_WithThreadFromAnotherTenant_ReturnsNotFound()
    {
        var thread = EmailThread.NewFromMessage(Guid.NewGuid(), Guid.NewGuid(), "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);
        var incomingEmails = new FakeIncomingEmailRepository();
        var drafts = new FakeDraftRepository();

        var result = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(Guid.NewGuid(), thread.Id, 1, 20),
            emailThreads,
            incomingEmails,
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailThread.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithUnknownThread_ReturnsNotFound()
    {
        var emailThreads = new FakeEmailThreadRepository();
        var incomingEmails = new FakeIncomingEmailRepository();
        var drafts = new FakeDraftRepository();

        var result = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(Guid.NewGuid(), Guid.NewGuid(), 1, 20),
            emailThreads,
            incomingEmails,
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailThread.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_NeverIncludesBodyFieldsInTheDto()
    {
        var properties = typeof(MessageSummary).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("HtmlBody", properties);
        Assert.DoesNotContain("TextBody", properties);
        Assert.DoesNotContain("Body", properties);
    }

    [Fact]
    public async Task Handle_WithOneInboundAndOneOutbound_InboundFirst_ReturnsBothInChronologicalOrder()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);

        var incomingEmails = new FakeIncomingEmailRepository();
        var inbound = NewIncomingEmail(tenantId, customerId, thread.Id, DateTime.UtcNow.AddMinutes(-10));
        await incomingEmails.AddAsync(inbound);

        var drafts = new FakeDraftRepository();
        var outbound = NewSentReplyDraft(tenantId, customerId, thread.Id, "to@example.com");
        await drafts.AddAsync(outbound);

        var result = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(tenantId, thread.Id, 1, 20),
            emailThreads,
            incomingEmails,
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal([inbound.Id, outbound.Id], result.Value.Items.Select(x => x.MessageId));

        var inboundItem = result.Value.Items[0];
        Assert.Equal(MessageDirection.Inbound, inboundItem.Direction);
        Assert.Null(inboundItem.ToAddresses);

        var outboundItem = result.Value.Items[1];
        Assert.Equal(MessageDirection.Outbound, outboundItem.Direction);
        Assert.Equal("Re: Original subject", outboundItem.Subject);
        Assert.Null(outboundItem.From);
        Assert.Equal(["to@example.com"], outboundItem.ToAddresses);
    }

    [Fact]
    public async Task Handle_WithOneInboundAndOneOutbound_OutboundFirst_ReturnsBothInChronologicalOrder()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);

        var drafts = new FakeDraftRepository();
        var outbound = NewSentReplyDraft(tenantId, customerId, thread.Id, "to@example.com");
        await drafts.AddAsync(outbound);

        var incomingEmails = new FakeIncomingEmailRepository();
        var inbound = NewIncomingEmail(tenantId, customerId, thread.Id, DateTime.UtcNow.AddMinutes(10));
        await incomingEmails.AddAsync(inbound);

        var result = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(tenantId, thread.Id, 1, 20),
            emailThreads,
            incomingEmails,
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal([outbound.Id, inbound.Id], result.Value.Items.Select(x => x.MessageId));
        Assert.Equal(MessageDirection.Outbound, result.Value.Items[0].Direction);
        Assert.Equal(MessageDirection.Inbound, result.Value.Items[1].Direction);
    }

    [Fact]
    public async Task Handle_WithDraftNotYetSentInSameThread_NeverIncludesIt()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);
        var incomingEmails = new FakeIncomingEmailRepository();

        var drafts = new FakeDraftRepository();

        var replyContext = ReplyContext.Create(Guid.NewGuid(), thread.Id, null, null, null).Value;
        var openDraft = Draft.CreateReply(tenantId, customerId, Guid.NewGuid(), replyContext, "Subject").Value;
        await drafts.AddAsync(openDraft);

        var failedReplyContext = ReplyContext.Create(Guid.NewGuid(), thread.Id, null, null, null).Value;
        var failedDraft = Draft.CreateReply(tenantId, customerId, Guid.NewGuid(), failedReplyContext, "Subject").Value;
        failedDraft.MarkSending();
        failedDraft.MarkFailed("Postmaster timeout");
        await drafts.AddAsync(failedDraft);

        var result = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(tenantId, thread.Id, 1, 20),
            emailThreads,
            incomingEmails,
            drafts,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task Handle_WithMergedResultSpanningTwoPages_PaginatesTheMergedListNotEachSource()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);

        var incomingEmails = new FakeIncomingEmailRepository();
        var drafts = new FakeDraftRepository();

        // Draft.MarkSent stamps UpdatedAtUtc from the real wall clock (no injectable clock on the
        // aggregate), so the 2 outbound anchors are captured AFTER creation and the 3 inbound
        // timestamps are derived from them — guarantees the intended interleave
        // (in0, outA, in2, outB, in4) regardless of how fast the test runs.
        var outA = NewSentReplyDraft(tenantId, customerId, thread.Id, "a@example.com");
        await drafts.AddAsync(outA);
        await Task.Delay(20);
        var outB = NewSentReplyDraft(tenantId, customerId, thread.Id, "b@example.com");
        await drafts.AddAsync(outB);

        var ta = outA.UpdatedAtUtc;
        var tb = outB.UpdatedAtUtc;
        Assert.True(tb > ta);
        var midpoint = ta + TimeSpan.FromTicks((tb - ta).Ticks / 2);

        var in0 = NewIncomingEmail(tenantId, customerId, thread.Id, ta.AddMinutes(-10));
        await incomingEmails.AddAsync(in0);
        var in2 = NewIncomingEmail(tenantId, customerId, thread.Id, midpoint);
        await incomingEmails.AddAsync(in2);
        var in4 = NewIncomingEmail(tenantId, customerId, thread.Id, tb.AddMinutes(10));
        await incomingEmails.AddAsync(in4);

        var page1 = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(tenantId, thread.Id, 1, 2),
            emailThreads,
            incomingEmails,
            drafts,
            CancellationToken.None
        );
        var page2 = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(tenantId, thread.Id, 2, 2),
            emailThreads,
            incomingEmails,
            drafts,
            CancellationToken.None
        );
        var page3 = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(tenantId, thread.Id, 3, 2),
            emailThreads,
            incomingEmails,
            drafts,
            CancellationToken.None
        );

        Assert.True(page1.IsSuccess && page2.IsSuccess && page3.IsSuccess);
        Assert.Equal(5, page1.Value.TotalCount);
        Assert.Equal(5, page2.Value.TotalCount);
        Assert.Equal(5, page3.Value.TotalCount);

        // page1/page2 have 2 items each, page3 has the remainder (1) — never "2 inbound + 2 outbound"
        // independently, the true merged chronological order across all 3 pages.
        Assert.Equal(2, page1.Value.Items.Count);
        Assert.Equal(2, page2.Value.Items.Count);
        Assert.Single(page3.Value.Items);

        var allIds = page1.Value.Items.Concat(page2.Value.Items).Concat(page3.Value.Items).Select(x => x.MessageId);
        Assert.Equal([in0.Id, outA.Id, in2.Id, outB.Id, in4.Id], allIds);
    }
}
