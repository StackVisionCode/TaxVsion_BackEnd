using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Delivery;
using TaxVision.Correspondence.Domain.Inbox;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Domain;

// Estado de entrega por destinatario y enlace a la conversación de un enviado (R2).
public sealed class DraftDeliveryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void A_result_updates_only_its_recipient_matching_without_case()
    {
        var draft = Sent(Tenant, null, Guid.NewGuid(), "Quote", "maria@x.com");
        var at = DateTime.UtcNow;

        var result = draft.RecordDeliveryOutcome("MARIA@x.com", DeliveryOutcome.NotDelivered, "InvalidAddress", at);

        Assert.True(result.Value);
        var recipient = Assert.Single(draft.Recipients);
        Assert.Equal(DeliveryOutcome.NotDelivered, recipient.DeliveryStatus);
        Assert.Equal("InvalidAddress", recipient.DeliveryCategory);
        Assert.Equal(at, recipient.DeliveryUpdatedAtUtc);
    }

    [Fact]
    public void An_older_or_repeated_result_does_not_overwrite_a_newer_one()
    {
        var draft = Sent(Tenant, null, Guid.NewGuid(), "Quote", "maria@x.com");
        var now = DateTime.UtcNow;
        draft.RecordDeliveryOutcome("maria@x.com", DeliveryOutcome.NotDelivered, "MailboxFull", now);

        var older = draft.RecordDeliveryOutcome("maria@x.com", DeliveryOutcome.Delayed, "Delayed", now.AddMinutes(-1));
        var repeated = draft.RecordDeliveryOutcome("maria@x.com", DeliveryOutcome.NotDelivered, "MailboxFull", now);

        Assert.False(older.Value);
        Assert.False(repeated.Value);
        Assert.Equal(DeliveryOutcome.NotDelivered, draft.Recipients.Single().DeliveryStatus);
    }

    [Fact]
    public void Delivered_clears_the_category()
    {
        var draft = Sent(Tenant, null, Guid.NewGuid(), "Quote", "maria@x.com");
        var now = DateTime.UtcNow;
        draft.RecordDeliveryOutcome("maria@x.com", DeliveryOutcome.Delayed, "Delayed", now);

        draft.RecordDeliveryOutcome("maria@x.com", DeliveryOutcome.Delivered, "Delayed", now.AddMinutes(1));

        Assert.Equal(DeliveryOutcome.Delivered, draft.Recipients.Single().DeliveryStatus);
        Assert.Null(draft.Recipients.Single().DeliveryCategory);
    }

    [Fact]
    public void An_unknown_recipient_or_an_unsent_draft_is_rejected()
    {
        var sent = Sent(Tenant, null, Guid.NewGuid(), "Quote", "maria@x.com");
        var open = Open(Tenant, null, Guid.NewGuid(), "Quote", "maria@x.com");

        Assert.Equal(
            "Draft.RecipientNotFound",
            sent.RecordDeliveryOutcome("luis@x.com", DeliveryOutcome.Delayed, null, DateTime.UtcNow).Error.Code
        );
        Assert.Equal(
            "Draft.InvalidTransition",
            open.RecordDeliveryOutcome("maria@x.com", DeliveryOutcome.Delayed, null, DateTime.UtcNow).Error.Code
        );
    }

    [Fact]
    public void A_sent_email_links_to_one_conversation_only()
    {
        var draft = Sent(Tenant, null, Guid.NewGuid(), "Quote", "maria@x.com");
        var threadId = Guid.NewGuid();

        Assert.True(draft.LinkToThread(threadId).IsSuccess);
        Assert.True(draft.LinkToThread(threadId).IsSuccess);
        Assert.Equal("Draft.AlreadyInThread", draft.LinkToThread(Guid.NewGuid()).Error.Code);
        Assert.Equal(threadId, draft.EmailThreadId);
        Assert.Equal(
            "Draft.InvalidTransition",
            Open(Tenant, null, Guid.NewGuid(), "x", "a@b.com").LinkToThread(threadId).Error.Code
        );
    }

    [Fact]
    public void A_conversation_for_a_sent_email_can_have_no_client_and_no_subject()
    {
        var id = Guid.NewGuid();
        var sentAt = DateTime.UtcNow.AddHours(-1);

        var thread = EmailThread.NewForSentEmail(id, Tenant, null, "  ", sentAt).Value;

        Assert.Equal(id, thread.Id);
        Assert.Null(thread.CustomerId);
        Assert.Equal(EmailThread.NoSubject, thread.Subject);
        Assert.Equal(0, thread.MessageCount);
        Assert.Equal(sentAt, thread.LastMessageAtUtc);
        Assert.Equal(
            "EmailThread.CustomerIdInvalid",
            EmailThread.NewForSentEmail(id, Tenant, Guid.Empty, "x", sentAt).Error.Code
        );
    }

    [Fact]
    public void A_not_delivered_notice_brings_an_archived_conversation_back_to_the_top()
    {
        var thread = EmailThread
            .NewForSentEmail(Guid.NewGuid(), Tenant, null, "Quote", DateTime.UtcNow.AddDays(-2))
            .Value;
        thread.Archive();
        var now = DateTime.UtcNow;

        Assert.True(thread.RegisterDeliveryNotice(now, requiresAction: true));

        Assert.Equal(EmailThreadStatus.Active, thread.Status);
        Assert.Equal(now, thread.LastMessageAtUtc);
    }

    [Fact]
    public void A_delayed_notice_neither_unarchives_nor_raises_the_conversation()
    {
        var sentAt = DateTime.UtcNow.AddDays(-2);
        var thread = EmailThread.NewForSentEmail(Guid.NewGuid(), Tenant, null, "Quote", sentAt).Value;
        thread.Archive();

        Assert.False(thread.RegisterDeliveryNotice(DateTime.UtcNow, requiresAction: false));

        Assert.Equal(EmailThreadStatus.Archived, thread.Status);
        Assert.Equal(sentAt, thread.LastMessageAtUtc);
    }
}
