using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;

namespace TaxVision.Correspondence.Tests.Threads;

public sealed class ListCustomerThreadsHandlerTests
{
    private static EmailThread NewThread(Guid tenantId, Guid customerId, DateTime lastMessageAtUtc) =>
        EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, lastMessageAtUtc).Value;

    private static IncomingEmail NewIncomingEmail(Guid tenantId, Guid customerId, Guid threadId, string providerMessageId) =>
        IncomingEmail
            .Create(
                tenantId,
                customerId,
                threadId,
                Guid.NewGuid(),
                "gmail",
                providerMessageId,
                EmailAddress.Create("customer@example.com").Value,
                "The Customer",
                "Subject",
                "Snippet",
                DateTime.UtcNow,
                hasAttachments: false,
                attachmentCount: 0
            )
            .Value;

    [Fact]
    public async Task Handle_WithThreeThreads_ReturnsAllOrderedByLastMessageAtUtcDescending()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var oldest = NewThread(tenantId, customerId, now.AddDays(-2));
        var middle = NewThread(tenantId, customerId, now.AddDays(-1));
        var newest = NewThread(tenantId, customerId, now);
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(oldest);
        await emailThreads.AddAsync(middle);
        await emailThreads.AddAsync(newest);

        var result = await ListCustomerThreadsHandler.Handle(
            new ListCustomerThreadsQuery(tenantId, customerId, 1, 20),
            emailThreads,
            new FakeIncomingEmailRepository(),
            CancellationToken.None
        );

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal([newest.Id, middle.Id, oldest.Id], result.Items.Select(x => x.ThreadId));
    }

    [Fact]
    public async Task Handle_WithPageTwoSizeOne_ReturnsTheMiddleItem()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var oldest = NewThread(tenantId, customerId, now.AddDays(-2));
        var middle = NewThread(tenantId, customerId, now.AddDays(-1));
        var newest = NewThread(tenantId, customerId, now);
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(oldest);
        await emailThreads.AddAsync(middle);
        await emailThreads.AddAsync(newest);

        var result = await ListCustomerThreadsHandler.Handle(
            new ListCustomerThreadsQuery(tenantId, customerId, 2, 1),
            emailThreads,
            new FakeIncomingEmailRepository(),
            CancellationToken.None
        );

        Assert.Equal(3, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(middle.Id, item.ThreadId);
        Assert.Equal(2, result.Page);
        Assert.Equal(1, result.Size);
    }

    [Fact]
    public async Task Handle_WithThreadFromAnotherTenant_NeverReturnsIt()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ownThread = NewThread(tenantId, customerId, DateTime.UtcNow);
        var otherTenantThread = NewThread(Guid.NewGuid(), customerId, DateTime.UtcNow);
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(ownThread);
        await emailThreads.AddAsync(otherTenantThread);

        var result = await ListCustomerThreadsHandler.Handle(
            new ListCustomerThreadsQuery(tenantId, customerId, 1, 20),
            emailThreads,
            new FakeIncomingEmailRepository(),
            CancellationToken.None
        );

        var item = Assert.Single(result.Items);
        Assert.Equal(ownThread.Id, item.ThreadId);
    }

    [Fact]
    public async Task Handle_MapsStatusAndCountsIntoTheSummary()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = NewThread(tenantId, customerId, DateTime.UtcNow);
        thread.AppendMessage(thread.LastMessageAtUtc.AddMinutes(5));
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);

        var result = await ListCustomerThreadsHandler.Handle(
            new ListCustomerThreadsQuery(tenantId, customerId, 1, 20),
            emailThreads,
            new FakeIncomingEmailRepository(),
            CancellationToken.None
        );

        var summary = Assert.Single(result.Items);
        Assert.Equal("Active", summary.Status);
        Assert.Equal(2, summary.MessageCount);
        Assert.Equal(thread.FirstMessageAtUtc, summary.FirstMessageAtUtc);
        Assert.Equal(thread.LastMessageAtUtc, summary.LastMessageAtUtc);
    }

    [Fact]
    public async Task Handle_CountsOnlyUnreadInboundPerThread()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = NewThread(tenantId, customerId, DateTime.UtcNow);
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);

        var unreadA = NewIncomingEmail(tenantId, customerId, thread.Id, "msg-a");
        var unreadB = NewIncomingEmail(tenantId, customerId, thread.Id, "msg-b");
        var alreadyRead = NewIncomingEmail(tenantId, customerId, thread.Id, "msg-c");
        alreadyRead.MarkRead(DateTime.UtcNow);
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(unreadA);
        await incomingEmails.AddAsync(unreadB);
        await incomingEmails.AddAsync(alreadyRead);

        var result = await ListCustomerThreadsHandler.Handle(
            new ListCustomerThreadsQuery(tenantId, customerId, 1, 20),
            emailThreads,
            incomingEmails,
            CancellationToken.None
        );

        var summary = Assert.Single(result.Items);
        Assert.Equal(2, summary.UnreadCount);
    }

    [Fact]
    public async Task Handle_WithNoIncomingEmails_ReportsZeroUnread()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = NewThread(tenantId, customerId, DateTime.UtcNow);
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);

        var result = await ListCustomerThreadsHandler.Handle(
            new ListCustomerThreadsQuery(tenantId, customerId, 1, 20),
            emailThreads,
            new FakeIncomingEmailRepository(),
            CancellationToken.None
        );

        var summary = Assert.Single(result.Items);
        Assert.Equal(0, summary.UnreadCount);
    }
}
