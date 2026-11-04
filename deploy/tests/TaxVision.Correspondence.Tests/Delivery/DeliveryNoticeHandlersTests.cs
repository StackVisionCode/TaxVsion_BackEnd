using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Application.Delivery;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Delivery;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Messages;
using TaxVision.Correspondence.Tests.Projections;
using static TaxVision.Correspondence.Tests.Search.MailSearchTestData;

namespace TaxVision.Correspondence.Tests.Delivery;

// Lectura y acciones sobre conversaciones con avisos de entrega (R2).
public sealed class DeliveryNoticeHandlersTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();

    private sealed record World(
        EmailThread Thread,
        Draft Sent,
        DeliveryNotice Notice,
        FakeEmailThreadRepository Threads,
        FakeDraftRepository Drafts,
        FakeDeliveryNoticeRepository Notices,
        FakeIncomingEmailRepository Incoming
    );

    // Conversación sin cliente nacida de un rebote: el enviado enlazado y su aviso.
    private static async Task<World> NotClientBounceAsync(
        DeliveryOutcome outcome = DeliveryOutcome.NotDelivered,
        Guid? reportAccountId = null
    )
    {
        var sent = Sent(Tenant, null, Account, "Quote", "maria@x.com");
        sent.RecordDeliveryOutcome("maria@x.com", outcome, "InvalidAddress", DateTime.UtcNow);
        var thread = EmailThread.NewForSentEmail(sent.Id, Tenant, null, sent.Subject, sent.UpdatedAtUtc).Value;
        sent.LinkToThread(thread.Id);
        var notice = DeliveryNotice
            .Create(
                Tenant,
                thread.Id,
                sent.Id,
                sent.SentMessageId!.Value,
                null,
                Account,
                "maria@x.com",
                outcome,
                outcome == DeliveryOutcome.Delayed ? "Delayed" : "InvalidAddress",
                "5.1.1",
                "550 user unknown",
                false,
                reportAccountId,
                reportAccountId is null ? null : "report-1",
                DateTime.UtcNow.AddSeconds(1),
                DateTime.UtcNow.AddSeconds(2)
            )
            .Value;
        thread.RegisterDeliveryNotice(notice.ReceivedAtUtc, notice.RequiresAction);

        var world = new World(thread, sent, notice, new(), new(), new(), new());
        await world.Threads.AddAsync(thread);
        await world.Drafts.AddAsync(sent);
        await world.Notices.AddAsync(notice);
        return world;
    }

    [Fact]
    public async Task Thread_messages_interleave_the_notice_as_its_own_kind_after_the_sent_email()
    {
        var w = await NotClientBounceAsync(reportAccountId: Account);

        var result = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(Tenant, w.Thread.Id, 1, 20),
            w.Threads,
            w.Incoming,
            w.Drafts,
            w.Notices,
            CancellationToken.None
        );

        Assert.Collection(
            result.Value.Items,
            sent =>
            {
                Assert.Equal(ThreadMessageKinds.Email, sent.Kind);
                Assert.Equal(MessageDirection.Outbound, sent.Direction);
                Assert.Equal("NotDelivered", sent.DeliveryStatus);
                Assert.Equal("NotDelivered", Assert.Single(sent.Deliveries!).Status);
            },
            notice =>
            {
                Assert.Equal(ThreadMessageKinds.DeliveryNotice, notice.Kind);
                Assert.Equal(w.Notice.Id, notice.MessageId);
                Assert.False(notice.IsRead);
                Assert.Equal("Quote", notice.Subject);
                Assert.Equal(DeliveryReasonCodes.InvalidAddress, notice.DeliveryNotice!.ReasonCode);
                Assert.Equal("maria@x.com", notice.DeliveryNotice.Recipient);
                Assert.True(notice.DeliveryNotice.CanViewOriginal);
            }
        );
    }

    [Fact]
    public async Task A_not_client_conversation_is_visible_to_a_restricted_user_through_the_mailbox_only()
    {
        var w = await NotClientBounceAsync();

        var asRestricted = await GetInboxThreadHandler.Handle(
            new GetInboxThreadQuery(Tenant, w.Thread.Id, RestrictToUserId: Guid.NewGuid()),
            w.Threads,
            w.Incoming,
            w.Notices,
            CancellationToken.None
        );
        w.Threads.HasVisibleMessageResult = false;
        var otherMailbox = await GetInboxThreadHandler.Handle(
            new GetInboxThreadQuery(Tenant, w.Thread.Id, VisibleAccountIds: [Guid.NewGuid()]),
            w.Threads,
            w.Incoming,
            w.Notices,
            CancellationToken.None
        );

        Assert.True(asRestricted.IsSuccess);
        Assert.Null(asRestricted.Value.CustomerId);
        Assert.Equal(w.Notice.Id, asRestricted.Value.LatestDeliveryNotice!.DeliveryNoticeId);
        Assert.Equal("NotDelivered", asRestricted.Value.LatestDeliveryNotice.Outcome);
        Assert.Equal("EmailThread.NotFound", otherMailbox.Error.Code);
    }

    [Fact]
    public async Task The_inbox_row_carries_the_latest_notice()
    {
        var w = await NotClientBounceAsync();

        var page = await ListInboxThreadsHandler.Handle(
            new ListInboxThreadsQuery(
                new InboxThreadFilter(Tenant, EmailThreadStatus.Active, null, false, null, 1, 20, null, Guid.NewGuid())
            ),
            w.Threads,
            w.Incoming,
            w.Notices,
            CancellationToken.None
        );

        var row = Assert.Single(page.Items);
        Assert.Null(row.CustomerId);
        Assert.Equal(DeliveryReasonCodes.InvalidAddress, row.LatestDeliveryNotice!.ReasonCode);
    }

    [Fact]
    public async Task Marking_the_thread_read_and_unread_covers_its_not_delivered_notices()
    {
        var w = await NotClientBounceAsync();
        var bus = new FakeMessageBus();

        await SetThreadReadStateHandler.Handle(
            new SetThreadReadStateCommand(Tenant, w.Thread.Id, IsRead: true),
            w.Threads,
            w.Incoming,
            w.Notices,
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );
        Assert.True(w.Notice.IsRead);

        await SetThreadReadStateHandler.Handle(
            new SetThreadReadStateCommand(Tenant, w.Thread.Id, IsRead: false),
            w.Threads,
            w.Incoming,
            w.Notices,
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );
        Assert.False(w.Notice.IsRead);
        Assert.Equal(2, bus.Published.Count);
    }

    [Fact]
    public async Task Archiving_marks_the_notices_read()
    {
        var w = await NotClientBounceAsync();

        await ArchiveThreadHandler.Handle(
            new ArchiveThreadCommand(Tenant, w.Thread.Id),
            w.Threads,
            w.Incoming,
            w.Notices,
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(w.Notice.IsRead);
        Assert.Equal(EmailThreadStatus.Archived, w.Thread.Status);
    }

    [Fact]
    public async Task The_original_notice_is_fetched_live_from_the_report_mailbox()
    {
        var reportAccount = Guid.NewGuid();
        var w = await NotClientBounceAsync(reportAccountId: reportAccount);
        var connectors = new FakeConnectorsClient();

        var result = await GetDeliveryNoticeOriginalHandler.Handle(
            new GetDeliveryNoticeOriginalQuery(Tenant, w.Notice.Id),
            w.Notices,
            w.Threads,
            connectors,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal((Tenant, reportAccount, "report-1"), Assert.Single(connectors.Calls));
    }

    [Fact]
    public async Task Without_a_report_mailbox_or_visibility_the_original_is_unavailable()
    {
        var w = await NotClientBounceAsync(reportAccountId: null);
        var connectors = new FakeConnectorsClient();

        var unavailable = await GetDeliveryNoticeOriginalHandler.Handle(
            new GetDeliveryNoticeOriginalQuery(Tenant, w.Notice.Id),
            w.Notices,
            w.Threads,
            connectors,
            CancellationToken.None
        );
        w.Threads.HasVisibleMessageResult = false;
        var hidden = await GetDeliveryNoticeOriginalHandler.Handle(
            new GetDeliveryNoticeOriginalQuery(Tenant, w.Notice.Id, VisibleAccountIds: [Guid.NewGuid()]),
            w.Notices,
            w.Threads,
            connectors,
            CancellationToken.None
        );

        Assert.Equal("DeliveryNotice.OriginalUnavailable", unavailable.Error.Code);
        Assert.Equal("DeliveryNotice.NotFound", hidden.Error.Code);
        Assert.Empty(connectors.Calls);
    }

    [Fact]
    public async Task The_sent_folder_shows_the_worst_delivery_status()
    {
        var drafts = new FakeDraftRepository();
        var draft = Draft.CreateNew(Tenant, null, Account, Guid.NewGuid()).Value;
        draft.AutoSave(
            "Two recipients",
            "<p>x</p>",
            "x",
            [
                new DraftRecipientData(
                    TaxVision.Correspondence.Domain.ValueObjects.EmailAddress.Create("a@x.com").Value,
                    EmailRecipientType.To,
                    null
                ),
                new DraftRecipientData(
                    TaxVision.Correspondence.Domain.ValueObjects.EmailAddress.Create("b@x.com").Value,
                    EmailRecipientType.Cc,
                    null
                ),
            ]
        );
        draft.MarkSending();
        draft.MarkSent(Guid.NewGuid());
        draft.RecordDeliveryOutcome("a@x.com", DeliveryOutcome.Delivered, null, DateTime.UtcNow);
        draft.RecordDeliveryOutcome("b@x.com", DeliveryOutcome.Delayed, "Delayed", DateTime.UtcNow);
        await drafts.AddAsync(draft);

        var page = await ListSentMessagesHandler.Handle(
            new ListSentMessagesQuery(Tenant, DraftCustomerScope.Unlinked, 1, 20),
            drafts,
            CancellationToken.None
        );

        var item = Assert.Single(page.Items);
        Assert.Equal("Delayed", item.DeliveryStatus);
        Assert.Equal(2, item.Deliveries!.Count);
        Assert.Equal("Delivered", item.Deliveries.Single(d => d.Address == "a@x.com").Status);
    }
}
