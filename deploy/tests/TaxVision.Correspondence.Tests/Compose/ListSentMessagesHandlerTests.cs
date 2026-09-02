using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class ListSentMessagesHandlerTests
{
    private static Draft NewSent(Guid tenantId, Guid customerId, string subject, string to, int attachments = 0)
    {
        var draft = Draft.CreateNew(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AutoSave(
            subject,
            "<p>hi</p>",
            "hi",
            [new DraftRecipientData(EmailAddress.Create(to).Value, EmailRecipientType.To, null)]
        );
        for (var i = 0; i < attachments; i++)
            draft.AttachFile(DraftAttachmentRef.Create(Guid.NewGuid(), $"file{i}.pdf", "application/pdf", 1024).Value);
        draft.MarkSending();
        draft.MarkSent(Guid.NewGuid());
        return draft;
    }

    [Fact]
    public async Task Handle_ReturnsOnlySentOrderedBySentAtDescending()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();

        var older = NewSent(tenantId, customerId, "Older", "moquetez671@gmail.com");
        await drafts.AddAsync(older);
        await Task.Delay(20);
        var newer = NewSent(tenantId, customerId, "Newer", "moquetez671@gmail.com");
        await drafts.AddAsync(newer);

        var open = Draft.CreateNew(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid()).Value;
        open.AutoSave("Open draft", null, null, null);
        await drafts.AddAsync(open);

        var result = await ListSentMessagesHandler.Handle(
            new ListSentMessagesQuery(tenantId, customerId, 1, 20),
            drafts,
            CancellationToken.None
        );

        Assert.Equal(2, result.TotalCount);
        Assert.Equal([newer.Id, older.Id], result.Items.Select(x => x.MessageId));
        Assert.All(result.Items, x => Assert.Contains("moquetez671@gmail.com", x.ToAddresses));
    }

    [Fact]
    public async Task Handle_FlagsAttachmentsAndReply()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();

        var withAttachments = NewSent(tenantId, customerId, "Con adjuntos", "a@b.com", attachments: 2);
        await drafts.AddAsync(withAttachments);

        var replyContext = ReplyContext.Create(Guid.NewGuid(), Guid.NewGuid(), null, null, null).Value;
        var reply = Draft
            .CreateReply(tenantId, customerId, Guid.NewGuid(), Guid.NewGuid(), replyContext, "Original")
            .Value;
        reply.AutoSave(
            "Re: algo",
            "<p>x</p>",
            "x",
            [new DraftRecipientData(EmailAddress.Create("a@b.com").Value, EmailRecipientType.To, null)]
        );
        reply.MarkSending();
        reply.MarkSent(Guid.NewGuid());
        await drafts.AddAsync(reply);

        var result = await ListSentMessagesHandler.Handle(
            new ListSentMessagesQuery(tenantId, customerId, 1, 20),
            drafts,
            CancellationToken.None
        );

        var attachRow = result.Items.Single(x => x.MessageId == withAttachments.Id);
        Assert.True(attachRow.HasAttachments);
        Assert.Equal(2, attachRow.AttachmentCount);
        Assert.False(attachRow.IsReply);
        Assert.Null(attachRow.EmailThreadId);

        var replyRow = result.Items.Single(x => x.MessageId == reply.Id);
        Assert.True(replyRow.IsReply);
        Assert.NotNull(replyRow.EmailThreadId);
    }

    [Fact]
    public async Task Handle_NeverLeaksOtherCustomerOrTenant()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();

        await drafts.AddAsync(NewSent(tenantId, customerId, "Mine", "a@b.com"));
        await drafts.AddAsync(NewSent(tenantId, Guid.NewGuid(), "Other customer", "a@b.com"));
        await drafts.AddAsync(NewSent(Guid.NewGuid(), customerId, "Other tenant", "a@b.com"));

        var result = await ListSentMessagesHandler.Handle(
            new ListSentMessagesQuery(tenantId, customerId, 1, 20),
            drafts,
            CancellationToken.None
        );

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Mine", result.Items.Single().Subject);
    }
}
