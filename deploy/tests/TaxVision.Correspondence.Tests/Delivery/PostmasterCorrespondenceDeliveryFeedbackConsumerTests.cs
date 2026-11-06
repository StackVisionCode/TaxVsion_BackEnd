using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Delivery;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Delivery;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Delivery;

public sealed class PostmasterCorrespondenceDeliveryFeedbackConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();

    private sealed class Harness
    {
        public FakeDraftRepository Drafts { get; } = new();
        public FakeEmailThreadRepository Threads { get; } = new();
        public FakeDeliveryNoticeRepository Notices { get; } = new();
        public FakeIncomingEmailRepository Incoming { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeMessageBus Bus { get; } = new();

        public async Task HandleAsync(PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent evt)
        {
            await PostmasterCorrespondenceDeliveryFeedbackConsumer.Handle(
                evt,
                Drafts,
                Threads,
                Notices,
                UnitOfWork,
                Bus,
                new FakeCorrelationContext(),
                NullLogger<DeliveryNotice>.Instance,
                CancellationToken.None
            );
            await DomainEventReplay.DispatchIntoAsync(Bus, Threads, Incoming);
        }
    }

    private static PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent Feedback(
        Draft draft,
        string outcome = CorrespondenceDeliveryOutcomes.NotDelivered,
        string? category = DeliveryFailureCategories.InvalidAddress,
        string recipient = "maria@x.com",
        Guid? reportAccountId = null,
        string? reportProviderMessageId = "report-1",
        Guid? sentMessageId = null
    ) =>
        new()
        {
            TenantId = Tenant,
            CorrelationId = "corr-1",
            SentMessageId = sentMessageId ?? draft.SentMessageId!.Value,
            CorrespondenceDraftId = draft.Id,
            Recipient = recipient,
            Outcome = outcome,
            Category = category,
            StatusCode = "5.1.1",
            DiagnosticSnippet = "550 5.1.1 user unknown",
            Suppressed = true,
            ReportAccountId = reportAccountId,
            ReportProviderMessageId = reportProviderMessageId,
            EventAtUtc = DateTime.UtcNow,
        };

    [Fact]
    public async Task A_not_delivered_new_email_gets_a_conversation_and_an_unread_notice()
    {
        var h = new Harness();
        var draft = Sent(Tenant, null, Account, "Quote", "maria@x.com", Author);
        await h.Drafts.AddAsync(draft);

        await h.HandleAsync(Feedback(draft, reportAccountId: Account));

        var thread = Assert.Single(h.Threads.All);
        Assert.Equal(draft.Id, thread.Id);
        Assert.Null(thread.CustomerId);
        Assert.Equal("Quote", thread.Subject);
        Assert.Equal(thread.Id, draft.EmailThreadId);
        var notice = Assert.Single(h.Notices.All);
        Assert.False(notice.IsRead);
        Assert.True(notice.CanViewOriginal);
        Assert.Equal(DeliveryOutcome.NotDelivered, draft.Recipients.Single().DeliveryStatus);
        Assert.Equal(1, h.UnitOfWork.SaveChangesCallCount);

        var published = Assert.Single(h.Bus.Published.OfType<CorrespondenceDeliveryNoticeReceivedIntegrationEvent>());
        Assert.Equal(notice.Id, published.DeliveryNoticeId);
        Assert.Equal(thread.Id, published.EmailThreadId);
        Assert.Equal(Author, published.SentByUserId);
        Assert.Equal(Account, published.MailboxAccountId);
        Assert.Null(published.CustomerId);
        Assert.Equal("corr-1", published.CorrelationId);
        var state = Assert.Single(h.Bus.Published.OfType<CorrespondenceThreadStateChangedIntegrationEvent>());
        Assert.Null(state.CustomerId);
    }

    [Fact]
    public async Task The_same_result_twice_records_one_notice()
    {
        var h = new Harness();
        var draft = Sent(Tenant, Guid.NewGuid(), Account, "Quote", "maria@x.com", Author);
        await h.Drafts.AddAsync(draft);
        var evt = Feedback(draft);

        await h.HandleAsync(evt);
        await h.HandleAsync(evt with { EventId = Guid.NewGuid(), Recipient = "MARIA@x.com" });

        Assert.Single(h.Notices.All);
        Assert.Single(h.Bus.Published.OfType<CorrespondenceDeliveryNoticeReceivedIntegrationEvent>());
    }

    [Fact]
    public async Task A_not_delivered_reply_brings_its_archived_conversation_back_to_the_inbox()
    {
        var h = new Harness();
        var customerId = Guid.NewGuid();
        var thread = Thread(Tenant, customerId, "W-2", DateTime.UtcNow.AddDays(-3));
        thread.Archive();
        await h.Threads.AddAsync(thread);
        var reply = Draft
            .CreateReply(
                Tenant,
                customerId,
                Account,
                Author,
                ReplyContext.Create(Guid.NewGuid(), thread.Id, null, null, null).Value,
                "W-2"
            )
            .Value;
        reply.AutoSave(
            null,
            "<p>x</p>",
            "x",
            [
                new DraftRecipientData(
                    TaxVision.Correspondence.Domain.ValueObjects.EmailAddress.Create("ana@client.com").Value,
                    EmailRecipientType.To,
                    null
                ),
            ]
        );
        reply.MarkSending();
        reply.MarkSent(Guid.NewGuid());
        await h.Drafts.AddAsync(reply);

        await h.HandleAsync(Feedback(reply, recipient: "ana@client.com"));

        Assert.Single(h.Threads.All);
        Assert.Equal(EmailThreadStatus.Active, thread.Status);
        Assert.True(thread.LastMessageAtUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(
            customerId,
            Assert.Single(h.Bus.Published.OfType<CorrespondenceDeliveryNoticeReceivedIntegrationEvent>()).CustomerId
        );
    }

    [Fact]
    public async Task A_delayed_result_is_informational_only()
    {
        var h = new Harness();
        var customerId = Guid.NewGuid();
        var thread = Thread(Tenant, customerId, "W-2", DateTime.UtcNow.AddDays(-3));
        thread.Archive();
        await h.Threads.AddAsync(thread);
        var reply = Draft
            .CreateReply(
                Tenant,
                customerId,
                Account,
                Author,
                ReplyContext.Create(Guid.NewGuid(), thread.Id, null, null, null).Value,
                "W-2"
            )
            .Value;
        reply.AutoSave(
            null,
            "<p>x</p>",
            "x",
            [
                new DraftRecipientData(
                    TaxVision.Correspondence.Domain.ValueObjects.EmailAddress.Create("ana@client.com").Value,
                    EmailRecipientType.To,
                    null
                ),
            ]
        );
        reply.MarkSending();
        reply.MarkSent(Guid.NewGuid());
        await h.Drafts.AddAsync(reply);
        var lastActivity = thread.LastMessageAtUtc;

        await h.HandleAsync(
            Feedback(reply, CorrespondenceDeliveryOutcomes.Delayed, DeliveryFailureCategories.Delayed, "ana@client.com")
        );

        Assert.Equal(EmailThreadStatus.Archived, thread.Status);
        Assert.Equal(lastActivity, thread.LastMessageAtUtc);
        Assert.True(Assert.Single(h.Notices.All).IsRead);
        Assert.Equal(DeliveryOutcome.Delayed, reply.Recipients.Single().DeliveryStatus);
    }

    [Fact]
    public async Task Delivered_only_updates_the_recipient_status()
    {
        var h = new Harness();
        var draft = Sent(Tenant, null, Account, "Quote", "maria@x.com", Author);
        await h.Drafts.AddAsync(draft);

        await h.HandleAsync(Feedback(draft, CorrespondenceDeliveryOutcomes.Delivered, category: null));

        Assert.Equal(DeliveryOutcome.Delivered, draft.Recipients.Single().DeliveryStatus);
        Assert.Null(draft.EmailThreadId);
        Assert.Empty(h.Threads.All);
        Assert.Empty(h.Notices.All);
        Assert.Empty(h.Bus.Published);
        Assert.Equal(1, h.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_webhook_result_cannot_show_the_original_notice()
    {
        var h = new Harness();
        var draft = Sent(Tenant, null, Account, "Quote", "maria@x.com", Author);
        await h.Drafts.AddAsync(draft);

        await h.HandleAsync(Feedback(draft, reportAccountId: null, reportProviderMessageId: null));

        Assert.False(Assert.Single(h.Notices.All).CanViewOriginal);
    }

    [Fact]
    public async Task An_unknown_outcome_is_dropped()
    {
        var h = new Harness();
        var draft = Sent(Tenant, null, Account, "Quote", "maria@x.com", Author);
        await h.Drafts.AddAsync(draft);

        await h.HandleAsync(Feedback(draft, outcome: "Complained"));

        Assert.Empty(h.Notices.All);
        Assert.Empty(h.Bus.Published);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }

    // COR-14: Postmaster puede avisar antes de que el borrador quede Sent; el rebote no se pierde.
    [Fact]
    public async Task Handle_DraftStillSending_ThrowsDependencyNotReady()
    {
        var h = new Harness();
        var draft = Open(Tenant, null, Account, "Quote", "maria@x.com", Author);
        draft.MarkSending();
        await h.Drafts.AddAsync(draft);

        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            h.HandleAsync(Feedback(draft, sentMessageId: Guid.NewGuid()))
        );

        Assert.Empty(h.Notices.All);
        Assert.Empty(h.Bus.Published);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_DraftStillOpen_ThrowsDependencyNotReady()
    {
        var h = new Harness();
        var draft = Open(Tenant, null, Account, "Quote", "maria@x.com", Author);
        await h.Drafts.AddAsync(draft);

        await Assert.ThrowsAsync<DependencyNotReadyException>(() =>
            h.HandleAsync(Feedback(draft, sentMessageId: Guid.NewGuid()))
        );

        Assert.Empty(h.Notices.All);
    }

    [Fact]
    public async Task Handle_DraftNotFound_ThrowsDependencyNotReady()
    {
        var h = new Harness();
        var draft = Sent(Tenant, null, Account, "Quote", "maria@x.com", Author);

        await Assert.ThrowsAsync<DependencyNotReadyException>(() => h.HandleAsync(Feedback(draft)));

        Assert.Empty(h.Notices.All);
    }

    // Estados terminales: no va a llegar a Sent, reintentar no sirve.
    [Fact]
    public async Task Handle_DraftDiscarded_IsDropped()
    {
        var h = new Harness();
        var draft = Open(Tenant, null, Account, "Quote", "maria@x.com", Author);
        draft.Discard();
        await h.Drafts.AddAsync(draft);

        await h.HandleAsync(Feedback(draft, sentMessageId: Guid.NewGuid()));

        Assert.Empty(h.Notices.All);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_DraftFailed_IsDropped()
    {
        var h = new Harness();
        var draft = Open(Tenant, null, Account, "Quote", "maria@x.com", Author);
        draft.MarkSending();
        draft.MarkFailed("Provider rejected the message.");
        await h.Drafts.AddAsync(draft);

        await h.HandleAsync(Feedback(draft, sentMessageId: Guid.NewGuid()));

        Assert.Empty(h.Notices.All);
        Assert.Equal(0, h.UnitOfWork.SaveChangesCallCount);
    }
}
