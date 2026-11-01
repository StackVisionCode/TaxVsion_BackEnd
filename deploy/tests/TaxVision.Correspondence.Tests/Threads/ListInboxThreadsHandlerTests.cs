using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Ingest;

namespace TaxVision.Correspondence.Tests.Threads;

// Inbox de la oficina: hilos de todos los clientes visibles, con lo necesario para decidir sin abrirlos.
public sealed class ListInboxThreadsHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static EmailThread NewThread(Guid customerId, DateTime lastMessageAtUtc, string subject = "Subject") =>
        EmailThread.NewFromMessage(TenantId, customerId, subject, null, lastMessageAtUtc).Value;

    private static IncomingEmail NewEmail(
        Guid customerId,
        Guid threadId,
        string from,
        string name,
        string snippet,
        DateTime receivedAtUtc,
        bool hasAttachments = false
    ) =>
        IncomingEmail
            .Create(
                TenantId,
                customerId,
                threadId,
                Guid.NewGuid(),
                "gmail",
                Guid.NewGuid().ToString("N"),
                EmailAddress.Create(from).Value,
                name,
                "Subject",
                snippet,
                receivedAtUtc,
                hasAttachments: hasAttachments,
                attachmentCount: hasAttachments ? 1 : 0
            )
            .Value;

    private static InboxThreadFilter Filter(Guid? restrictTo = null, Guid? customerId = null) =>
        new(TenantId, EmailThreadStatus.Active, customerId, false, null, 1, 20, null, restrictTo);

    [Fact]
    public async Task Lists_threads_of_every_client_newest_first_with_sender_preview_and_unread()
    {
        var ana = Guid.NewGuid();
        var luis = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var older = NewThread(luis, now.AddHours(-3), "Reconectar");
        var newer = NewThread(ana, now, "W-2 for 2026");
        var threads = new FakeEmailThreadRepository();
        await threads.AddAsync(older);
        await threads.AddAsync(newer);
        var emails = new FakeIncomingEmailRepository();
        await emails.AddAsync(NewEmail(ana, newer.Id, "ana@mail.com", "Ana Pérez", "First", now.AddMinutes(-30)));
        await emails.AddAsync(
            NewEmail(ana, newer.Id, "ana@mail.com", "Ana Pérez", "Here it is", now, hasAttachments: true)
        );
        await emails.AddAsync(NewEmail(luis, older.Id, "luis@mail.com", "Luis Mena", "Gracias", now.AddHours(-3)));

        var page = await ListInboxThreadsHandler.Handle(
            new ListInboxThreadsQuery(Filter()),
            threads,
            emails,
            CancellationToken.None
        );

        Assert.Equal([newer.Id, older.Id], page.Items.Select(item => item.ThreadId));
        var first = page.Items[0];
        Assert.Equal(ana, first.CustomerId);
        Assert.Equal("Ana Pérez", first.LastSenderName);
        Assert.Equal("ana@mail.com", first.LastSenderAddress);
        Assert.Equal("Here it is", first.Snippet);
        Assert.True(first.HasAttachments);
        Assert.Equal(2, first.UnreadCount);
        Assert.False(page.Items[1].HasAttachments);
    }

    [Fact]
    public async Task Without_view_all_only_assigned_clients_appear()
    {
        var mine = Guid.NewGuid();
        var notMine = Guid.NewGuid();
        var preparer = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var visible = NewThread(mine, DateTime.UtcNow);
        await threads.AddAsync(visible);
        await threads.AddAsync(NewThread(notMine, DateTime.UtcNow));
        threads.Assignments.Add((preparer, mine));

        var page = await ListInboxThreadsHandler.Handle(
            new ListInboxThreadsQuery(Filter(restrictTo: preparer)),
            threads,
            new FakeIncomingEmailRepository(),
            CancellationToken.None
        );

        Assert.Equal([visible.Id], page.Items.Select(item => item.ThreadId));
    }

    [Fact]
    public async Task The_per_client_list_is_empty_for_a_client_that_is_not_assigned()
    {
        var notMine = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        await threads.AddAsync(NewThread(notMine, DateTime.UtcNow));

        var page = await ListCustomerThreadsHandler.Handle(
            new ListCustomerThreadsQuery(TenantId, notMine, 1, 20, null, Guid.NewGuid()),
            threads,
            new FakeIncomingEmailRepository(),
            CancellationToken.None
        );

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task A_thread_of_a_client_that_is_not_assigned_reads_as_not_found()
    {
        var thread = NewThread(Guid.NewGuid(), DateTime.UtcNow);
        var threads = new FakeEmailThreadRepository();
        await threads.AddAsync(thread);

        var result = await ListThreadMessagesHandler.Handle(
            new ListThreadMessagesQuery(TenantId, thread.Id, 1, 20, null, Guid.NewGuid()),
            threads,
            new FakeIncomingEmailRepository(),
            new FakeDraftRepository(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailThread.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task The_unread_count_comes_from_the_same_visible_threads()
    {
        var threads = new FakeEmailThreadRepository { UnreadCountResult = new UnreadMailCount(2, 3) };

        var count = await GetUnreadMailCountHandler.Handle(
            new GetUnreadMailCountQuery(TenantId, null, null),
            threads,
            CancellationToken.None
        );

        Assert.Equal(new UnreadMailCount(2, 3), count);
    }
}
