using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Application.Trash;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Trash;

public sealed class SoftDeleteHandlersTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();

    private static IncomingEmail NewIncoming(Guid threadId, bool read = false)
    {
        var email = IncomingEmail
            .Create(
                Tenant,
                Customer,
                threadId,
                Guid.NewGuid(),
                "Gmail",
                Guid.NewGuid().ToString(),
                EmailAddress.Create("client@example.com").Value,
                null,
                "Subject",
                "snippet",
                DateTime.UtcNow,
                false,
                0
            )
            .Value;
        if (read)
            email.MarkRead(DateTime.UtcNow);
        return email;
    }

    private static Draft NewSent()
    {
        var draft = Draft.CreateNew(Tenant, Customer, Guid.NewGuid(), Guid.NewGuid()).Value;
        draft.AutoSave(
            "Sent subj",
            "<p>x</p>",
            "x",
            [new DraftRecipientData(EmailAddress.Create("a@b.com").Value, EmailRecipientType.To, null)]
        );
        draft.MarkSending();
        draft.MarkSent(Guid.NewGuid());
        return draft;
    }

    [Fact]
    public async Task Archive_marks_the_thread_read()
    {
        var thread = EmailThread.NewFromMessage(Tenant, Customer, "Subject", null, DateTime.UtcNow).Value;
        var threads = new FakeEmailThreadRepository();
        await threads.AddAsync(thread);
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(NewIncoming(thread.Id));

        var result = await ArchiveThreadHandler.Handle(
            new ArchiveThreadCommand(Tenant, thread.Id),
            threads,
            incoming,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.All(incoming.All, e => Assert.True(e.IsRead));
    }

    [Fact]
    public async Task Unarchive_returns_the_thread_to_active()
    {
        var thread = EmailThread.NewFromMessage(Tenant, Customer, "Subject", null, DateTime.UtcNow).Value;
        thread.Archive();
        var threads = new FakeEmailThreadRepository();
        await threads.AddAsync(thread);

        var result = await UnarchiveThreadHandler.Handle(
            new UnarchiveThreadCommand(Tenant, thread.Id),
            threads,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailThreadStatus.Active, thread.Status);
        Assert.Null(thread.ArchivedAtUtc);
    }

    [Fact]
    public async Task Trash_then_restore_an_incoming_message_and_adjusts_MessageCount()
    {
        var thread = EmailThread.NewFromMessage(Tenant, Customer, "Subject", null, DateTime.UtcNow).Value; // MessageCount = 1
        var threads = new FakeEmailThreadRepository();
        await threads.AddAsync(thread);
        var email = NewIncoming(thread.Id);
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(email);
        var uow = new FakeUnitOfWork();

        await TrashIncomingMessageHandler.Handle(
            new TrashIncomingMessageCommand(Tenant, email.Id),
            incoming,
            threads,
            uow,
            CancellationToken.None
        );
        Assert.True(email.IsDeleted);
        Assert.Equal(0, thread.MessageCount);

        await RestoreIncomingMessageHandler.Handle(
            new RestoreIncomingMessageCommand(Tenant, email.Id),
            incoming,
            threads,
            uow,
            CancellationToken.None
        );
        Assert.False(email.IsDeleted);
        Assert.Equal(1, thread.MessageCount);
    }

    [Fact]
    public async Task Trashing_the_same_message_twice_decrements_MessageCount_once()
    {
        var thread = EmailThread.NewFromMessage(Tenant, Customer, "Subject", null, DateTime.UtcNow).Value;
        thread.AppendMessage(DateTime.UtcNow); // MessageCount = 2
        var threads = new FakeEmailThreadRepository();
        await threads.AddAsync(thread);
        var email = NewIncoming(thread.Id);
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(email);
        var uow = new FakeUnitOfWork();

        await TrashIncomingMessageHandler.Handle(
            new TrashIncomingMessageCommand(Tenant, email.Id),
            incoming,
            threads,
            uow,
            CancellationToken.None
        );
        await TrashIncomingMessageHandler.Handle(
            new TrashIncomingMessageCommand(Tenant, email.Id),
            incoming,
            threads,
            uow,
            CancellationToken.None
        );

        Assert.Equal(1, thread.MessageCount);
    }

    [Fact]
    public async Task Purge_incoming_fails_when_not_trashed()
    {
        var email = NewIncoming(Guid.NewGuid());
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(email);

        var result = await PurgeIncomingMessageHandler.Handle(
            new PurgeIncomingMessageCommand(Tenant, email.Id),
            incoming,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotTrashed", result.Error.Code);
    }

    [Fact]
    public async Task Purge_incoming_removes_it_when_trashed()
    {
        var email = NewIncoming(Guid.NewGuid());
        email.SoftDelete(DateTime.UtcNow);
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(email);

        var result = await PurgeIncomingMessageHandler.Handle(
            new PurgeIncomingMessageCommand(Tenant, email.Id),
            incoming,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(incoming.All);
    }

    [Fact]
    public async Task Trash_sent_requires_a_sent_draft()
    {
        var draft = Draft.CreateNew(Tenant, Customer, Guid.NewGuid(), Guid.NewGuid()).Value; // Draft, no Sent
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(draft);

        var result = await TrashSentMessageHandler.Handle(
            new TrashSentMessageCommand(Tenant, draft.Id),
            drafts,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure); // InvalidTransition
    }

    [Fact]
    public async Task ListTrash_unifies_incoming_and_sent_newest_first()
    {
        var incoming = new FakeIncomingEmailRepository();
        var drafts = new FakeDraftRepository();

        var email = NewIncoming(Guid.NewGuid());
        email.SoftDelete(DateTime.UtcNow);
        await incoming.AddAsync(email);

        var sent = NewSent();
        sent.SoftDelete(DateTime.UtcNow.AddSeconds(1));
        await drafts.AddAsync(sent);

        var result = await ListTrashHandler.Handle(
            new ListTrashQuery(Tenant, Customer, 1, 20),
            incoming,
            drafts,
            CancellationToken.None
        );

        Assert.Equal(2, result.TotalCount);
        Assert.Equal("Sent", result.Items[0].Kind); // más reciente primero
        Assert.Equal("Incoming", result.Items[1].Kind);
    }
}
