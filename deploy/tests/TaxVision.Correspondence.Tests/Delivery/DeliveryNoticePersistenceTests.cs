using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Delivery;
using TaxVision.Correspondence.Domain.Delivery;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Delivery;

// El consumer y los conteos contra el DbContext real (InMemory): inserta (no UPDATE) y los no leídos incluyen avisos.
public sealed class DeliveryNoticePersistenceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();

    private static Task RunConsumerAsync(
        CorrespondenceDbContext db,
        PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent evt
    ) =>
        PostmasterCorrespondenceDeliveryFeedbackConsumer.Handle(
            evt,
            new DraftRepository(db),
            new EmailThreadRepository(db),
            new DeliveryNoticeRepository(db),
            db,
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger<DeliveryNotice>.Instance,
            CancellationToken.None
        );

    private static PostmasterCorrespondenceDeliveryFeedbackIntegrationEvent Feedback(
        Guid draftId,
        Guid sentMessageId,
        string outcome = CorrespondenceDeliveryOutcomes.NotDelivered
    ) =>
        new()
        {
            TenantId = Tenant,
            SentMessageId = sentMessageId,
            CorrespondenceDraftId = draftId,
            Recipient = "maria@x.com",
            Outcome = outcome,
            Category = outcome == CorrespondenceDeliveryOutcomes.Delayed ? "Delayed" : "InvalidAddress",
            Suppressed = false,
            EventAtUtc = DateTime.UtcNow,
        };

    [Fact]
    public async Task A_bounce_of_a_new_email_persists_the_conversation_the_link_the_status_and_the_notice()
    {
        var databaseName = Guid.NewGuid().ToString();
        var sent = Sent(Tenant, null, Account, "Quote", "maria@x.com");
        await using (var db = NewContext(Tenant, databaseName))
        {
            db.Drafts.Add(sent);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext(Tenant, databaseName))
            await RunConsumerAsync(db, Feedback(sent.Id, sent.SentMessageId!.Value));

        await using (var db = NewContext(Tenant, databaseName))
        {
            var draft = (await new DraftRepository(db).GetByIdAsync(Tenant, sent.Id))!;
            Assert.Equal(sent.Id, draft.EmailThreadId);
            Assert.Equal(DeliveryOutcome.NotDelivered, draft.Recipients.Single().DeliveryStatus);
            var thread = await db.EmailThreads.SingleAsync();
            Assert.Null(thread.CustomerId);
            var notice = await db.DeliveryNotices.SingleAsync();
            Assert.Equal(thread.Id, notice.EmailThreadId);
            Assert.False(notice.IsRead);

            // Visible en el Inbox, aunque el usuario solo vea clientes asignados (D-1/D-2), y cuenta como no leído.
            var threads = new EmailThreadRepository(db);
            var unreadOnly = await threads.ListVisibleAsync(
                new InboxThreadFilter(Tenant, EmailThreadStatus.Active, null, true, null, 1, 20, null, Guid.NewGuid())
            );
            Assert.Equal(thread.Id, Assert.Single(unreadOnly.Items).Id);
            var count = await threads.CountUnreadAsync(Tenant, null, Guid.NewGuid());
            Assert.Equal(1, count.Threads);
            Assert.Equal(1, count.Messages);
            var perThread = await new IncomingEmailRepository(db).GetUnreadCountsByThreadAsync(Tenant, [thread.Id]);
            Assert.Equal(1, perThread[thread.Id]);
        }

        // Reentrega del mismo resultado: sin duplicar.
        await using (var db = NewContext(Tenant, databaseName))
            await RunConsumerAsync(db, Feedback(sent.Id, sent.SentMessageId!.Value));
        await using (var db = NewContext(Tenant, databaseName))
            Assert.Equal(1, await db.DeliveryNotices.CountAsync());
    }

    [Fact]
    public async Task A_delayed_notice_does_not_count_as_unread()
    {
        var databaseName = Guid.NewGuid().ToString();
        var sent = Sent(Tenant, null, Account, "Quote", "maria@x.com");
        await using (var db = NewContext(Tenant, databaseName))
        {
            db.Drafts.Add(sent);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext(Tenant, databaseName))
            await RunConsumerAsync(
                db,
                Feedback(sent.Id, sent.SentMessageId!.Value, CorrespondenceDeliveryOutcomes.Delayed)
            );

        await using (var db = NewContext(Tenant, databaseName))
        {
            Assert.Equal(1, await db.DeliveryNotices.CountAsync());
            var count = await new EmailThreadRepository(db).CountUnreadAsync(Tenant, null, null);
            Assert.Equal(0, count.Threads);
        }
    }

    [Fact]
    public async Task The_mailbox_gate_hides_a_not_client_conversation_sent_from_another_mailbox()
    {
        var databaseName = Guid.NewGuid().ToString();
        var sent = Sent(Tenant, null, Account, "Quote", "maria@x.com");
        await using (var db = NewContext(Tenant, databaseName))
        {
            db.Drafts.Add(sent);
            await db.SaveChangesAsync();
        }
        await using (var db = NewContext(Tenant, databaseName))
            await RunConsumerAsync(db, Feedback(sent.Id, sent.SentMessageId!.Value));

        await using (var db = NewContext(Tenant, databaseName))
        {
            var threads = new EmailThreadRepository(db);
            var other = await threads.ListVisibleAsync(
                new InboxThreadFilter(Tenant, null, null, false, null, 1, 20, [Guid.NewGuid()], null)
            );
            var owner = await threads.ListVisibleAsync(
                new InboxThreadFilter(Tenant, null, null, false, null, 1, 20, [Account], null)
            );
            Assert.Empty(other.Items);
            Assert.Single(owner.Items);
        }
    }

    [Fact]
    public async Task Latest_notice_and_existence_lookups_normalize_the_recipient()
    {
        await using var db = NewContext(Tenant);
        var threadId = Guid.NewGuid();
        var sentMessageId = Guid.NewGuid();
        var older = DeliveryNotice
            .Create(
                Tenant,
                threadId,
                Guid.NewGuid(),
                sentMessageId,
                null,
                Account,
                "Maria@X.com",
                DeliveryOutcome.Delayed,
                "Delayed",
                null,
                null,
                false,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMinutes(-5)
            )
            .Value;
        var newer = DeliveryNotice
            .Create(
                Tenant,
                threadId,
                Guid.NewGuid(),
                sentMessageId,
                null,
                Account,
                "maria@x.com",
                DeliveryOutcome.NotDelivered,
                "MailboxFull",
                null,
                null,
                false,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow
            )
            .Value;
        db.DeliveryNotices.AddRange(older, newer);
        await db.SaveChangesAsync();
        var repository = new DeliveryNoticeRepository(db);

        Assert.True(await repository.ExistsAsync(Tenant, sentMessageId, " MARIA@x.com ", DeliveryOutcome.Delayed));
        Assert.False(await repository.ExistsAsync(Tenant, Guid.NewGuid(), "maria@x.com", DeliveryOutcome.Delayed));
        Assert.Equal(newer.Id, (await repository.GetLatestByThreadAsync(Tenant, [threadId]))[threadId].Id);
    }
}
