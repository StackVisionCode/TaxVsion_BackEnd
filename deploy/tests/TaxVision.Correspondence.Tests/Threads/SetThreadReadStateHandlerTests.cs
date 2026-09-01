using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Threads;

public sealed class SetThreadReadStateHandlerTests
{
    private static IncomingEmail NewEmail(Guid tenantId, Guid customerId, Guid threadId, string providerMessageId) =>
        IncomingEmail
            .Create(
                tenantId,
                customerId,
                threadId,
                Guid.NewGuid(),
                "gmail",
                providerMessageId,
                EmailAddress.Create("customer@example.com").Value,
                null,
                "Subject",
                "Snippet",
                DateTime.UtcNow,
                hasAttachments: false,
                attachmentCount: 0
            )
            .Value;

    [Fact]
    public async Task Handle_MarksEveryInboundInThreadRead()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);

        var a = NewEmail(tenantId, customerId, thread.Id, "msg-a");
        var b = NewEmail(tenantId, customerId, thread.Id, "msg-b");
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(a);
        await incomingEmails.AddAsync(b);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetThreadReadStateHandler.Handle(
            new SetThreadReadStateCommand(tenantId, thread.Id, IsRead: true),
            emailThreads,
            incomingEmails,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(a.IsRead);
        Assert.True(b.IsRead);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_MarksEveryInboundInThreadUnread()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);

        var a = NewEmail(tenantId, customerId, thread.Id, "msg-a");
        a.MarkRead(DateTime.UtcNow);
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(a);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetThreadReadStateHandler.Handle(
            new SetThreadReadStateCommand(tenantId, thread.Id, IsRead: false),
            emailThreads,
            incomingEmails,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(a.IsRead);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WhenNothingChanges_DoesNotPersist()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);

        var a = NewEmail(tenantId, customerId, thread.Id, "msg-a");
        a.MarkRead(DateTime.UtcNow);
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(a);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetThreadReadStateHandler.Handle(
            new SetThreadReadStateCommand(tenantId, thread.Id, IsRead: true),
            emailThreads,
            incomingEmails,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithUnknownThread_ReturnsNotFound()
    {
        var result = await SetThreadReadStateHandler.Handle(
            new SetThreadReadStateCommand(Guid.NewGuid(), Guid.NewGuid(), IsRead: true),
            new FakeEmailThreadRepository(),
            new FakeIncomingEmailRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailThread.NotFound", result.Error.Code);
    }
}
