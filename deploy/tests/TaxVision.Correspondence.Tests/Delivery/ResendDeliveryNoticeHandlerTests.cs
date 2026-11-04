using TaxVision.Correspondence.Application.Delivery;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Delivery;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Delivery;

// Reenvío desde la tarjeta de aviso: borrador clonado, solo para ese destinatario y en la misma conversación.
public sealed class ResendDeliveryNoticeHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    private sealed record World(
        Draft Original,
        EmailThread Thread,
        DeliveryNotice Notice,
        FakeDraftRepository Drafts,
        FakeEmailThreadRepository Threads,
        FakeDeliveryNoticeRepository Notices,
        FakeUnitOfWork UnitOfWork
    );

    // Correo a dos destinatarios; rebota uno de ellos.
    private static async Task<World> BounceAsync(Guid? customerId = null)
    {
        var original = Draft.CreateNew(Tenant, customerId, Account, Guid.NewGuid()).Value;
        original.AutoSave(
            "Your documents",
            "<p>Attached</p>",
            "Attached",
            [
                new DraftRecipientData(EmailAddress.Create("maria@x.com").Value, EmailRecipientType.To, "Maria"),
                new DraftRecipientData(EmailAddress.Create("luis@x.com").Value, EmailRecipientType.Cc, null),
            ]
        );
        original.AttachFile(File("w2.pdf"));
        original.MarkSending();
        original.MarkSent(Guid.NewGuid());
        var thread = EmailThread
            .NewForSentEmail(original.Id, Tenant, customerId, original.Subject, original.UpdatedAtUtc)
            .Value;
        original.LinkToThread(thread.Id);
        var notice = DeliveryNotice
            .Create(
                Tenant,
                thread.Id,
                original.Id,
                original.SentMessageId!.Value,
                customerId,
                Account,
                "maria@x.com",
                DeliveryOutcome.NotDelivered,
                "InvalidAddress",
                "5.1.1",
                null,
                false,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow
            )
            .Value;

        var world = new World(original, thread, notice, new(), new(), new(), new());
        await world.Drafts.AddAsync(original);
        await world.Threads.AddAsync(thread);
        await world.Notices.AddAsync(notice);
        return world;
    }

    private static Task<BuildingBlocks.Results.Result<ResendDeliveryNoticeResult>> HandleAsync(
        World w,
        string? recipient = null,
        IReadOnlyCollection<Guid>? visible = null,
        Guid? restrictTo = null
    ) =>
        ResendDeliveryNoticeHandler.Handle(
            new ResendDeliveryNoticeCommand(Tenant, w.Notice.Id, recipient, Actor, visible, restrictTo),
            w.Notices,
            w.Threads,
            w.Drafts,
            w.UnitOfWork,
            CancellationToken.None
        );

    [Fact]
    public async Task Resending_to_the_same_address_clones_the_email_for_that_recipient_only()
    {
        var w = await BounceAsync();

        var result = await HandleAsync(w);

        var draft = w.Drafts.All.Single(d => d.Id == result.Value.DraftId);
        Assert.Equal(DraftStatus.Draft, draft.Status);
        Assert.Equal(Actor, draft.CreatedByUserId);
        Assert.Equal(w.Original.Subject, draft.Subject);
        Assert.Equal(w.Original.HtmlBody, draft.HtmlBody);
        Assert.Equal(w.Original.AccountId, draft.AccountId);
        Assert.Equal(w.Thread.Id, draft.EmailThreadId);
        var to = Assert.Single(draft.Recipients);
        Assert.Equal("maria@x.com", to.Address);
        Assert.Equal(EmailRecipientType.To, to.Type);
        Assert.Equal("Maria", to.DisplayName);
        var file = Assert.Single(draft.Attachments);
        Assert.Equal(w.Original.Attachments.Single().FileId, file.FileId);
        Assert.NotEqual(w.Original.Attachments.Single().Id, file.Id);
        Assert.Equal(1, w.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_corrected_address_replaces_the_bounced_one()
    {
        var w = await BounceAsync(Guid.NewGuid());

        var result = await HandleAsync(w, " Maria.Lopez@x.com ");

        var draft = w.Drafts.All.Single(d => d.Id == result.Value.DraftId);
        Assert.Equal("maria.lopez@x.com", Assert.Single(draft.Recipients).Address);
        Assert.Equal(w.Original.CustomerId, draft.CustomerId);
    }

    [Fact]
    public async Task An_invalid_address_is_rejected_without_saving()
    {
        var w = await BounceAsync();

        var result = await HandleAsync(w, "not-an-email");

        Assert.Equal("EmailAddress.Invalid", result.Error.Code);
        Assert.Equal(0, w.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_hidden_conversation_is_not_found_and_another_mailbox_cannot_be_used()
    {
        var w = await BounceAsync(Guid.NewGuid());

        var hidden = await HandleAsync(w, restrictTo: Guid.NewGuid());
        var otherMailbox = await HandleAsync(w, visible: [Guid.NewGuid()]);

        Assert.Equal("DeliveryNotice.NotFound", hidden.Error.Code);
        Assert.Equal("Draft.AccountNotVisible", otherMailbox.Error.Code);
        Assert.Single(w.Drafts.All);
    }

    [Fact]
    public async Task A_reply_keeps_its_reply_context_so_it_stays_in_the_thread()
    {
        var thread = Thread(Tenant, Guid.NewGuid(), "W-2");
        var context = ReplyContext.Create(Guid.NewGuid(), thread.Id, "<msg@client>", null, "provider-1").Value;
        var reply = Draft.CreateReply(Tenant, thread.CustomerId!.Value, Account, Guid.NewGuid(), context, "W-2").Value;
        reply.AutoSave(
            null,
            "<p>x</p>",
            "x",
            [new DraftRecipientData(EmailAddress.Create("ana@client.com").Value, EmailRecipientType.To, null)]
        );
        reply.MarkSending();
        reply.MarkSent(Guid.NewGuid());

        var resend = Draft.CreateResend(reply, EmailAddress.Create("ana@client.com").Value, Actor, thread.Id).Value;

        Assert.Same(context, resend.ReplyContext);
        Assert.Equal(thread.Id, resend.EmailThreadId);
        Assert.Equal("Re: W-2", resend.Subject);
    }

    [Fact]
    public void Only_a_sent_email_can_be_resent()
    {
        var open = Open(Tenant, null, Account, "x", "a@b.com");

        var result = Draft.CreateResend(open, EmailAddress.Create("a@b.com").Value, Actor, Guid.NewGuid());

        Assert.Equal("Draft.ResendRequiresSent", result.Error.Code);
    }

    [Fact]
    public async Task The_cloned_draft_and_its_attachment_rows_are_inserted_in_the_database()
    {
        var databaseName = Guid.NewGuid().ToString();
        var w = await BounceAsync();
        await using (var db = NewContext(Tenant, databaseName))
        {
            db.AddRange(w.Original, w.Thread, w.Notice);
            await db.SaveChangesAsync();
        }

        Guid draftId;
        await using (var db = NewContext(Tenant, databaseName))
        {
            var result = await ResendDeliveryNoticeHandler.Handle(
                new ResendDeliveryNoticeCommand(Tenant, w.Notice.Id, "fixed@x.com", Actor),
                new TaxVision.Correspondence.Infrastructure.Persistence.Repositories.DeliveryNoticeRepository(db),
                new TaxVision.Correspondence.Infrastructure.Persistence.Repositories.EmailThreadRepository(db),
                new TaxVision.Correspondence.Infrastructure.Persistence.Repositories.DraftRepository(db),
                db,
                CancellationToken.None
            );
            draftId = result.Value.DraftId;
        }

        await using (var db = NewContext(Tenant, databaseName))
        {
            var draft = (
                await new TaxVision.Correspondence.Infrastructure.Persistence.Repositories.DraftRepository(
                    db
                ).GetByIdAsync(Tenant, draftId)
            )!;
            Assert.Equal("fixed@x.com", Assert.Single(draft.Recipients).Address);
            Assert.Equal(w.Original.Attachments.Single().FileId, Assert.Single(draft.Attachments).FileId);
            Assert.Equal(2, db.DraftAttachments.Count());
        }
    }
}
