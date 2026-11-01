using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Messages;

public sealed class SetMessageReadStateHandlerTests
{
    private sealed record Setup(
        Guid TenantId,
        EmailThread Thread,
        IncomingEmail Email,
        FakeEmailThreadRepository Threads,
        FakeIncomingEmailRepository Emails
    );

    private static async Task<Setup> NewSetupAsync(bool read = false)
    {
        var tenantId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, Guid.NewGuid(), "Subject", null, DateTime.UtcNow).Value;
        var email = IncomingEmail
            .Create(
                tenantId,
                thread.CustomerId,
                thread.Id,
                Guid.NewGuid(),
                "gmail",
                "provider-msg-1",
                EmailAddress.Create("customer@example.com").Value,
                null,
                "Subject",
                "Snippet",
                DateTime.UtcNow,
                hasAttachments: false,
                attachmentCount: 0
            )
            .Value;
        if (read)
            email.MarkRead(DateTime.UtcNow);
        var threads = new FakeEmailThreadRepository();
        await threads.AddAsync(thread);
        var emails = new FakeIncomingEmailRepository();
        await emails.AddAsync(email);
        return new Setup(tenantId, thread, email, threads, emails);
    }

    [Fact]
    public async Task Handle_MarksReadPersistsAndPublishesTheThreadState()
    {
        var setup = await NewSetupAsync();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var actor = Guid.NewGuid();

        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(setup.TenantId, setup.Email.Id, IsRead: true, ActorUserId: actor),
            setup.Emails,
            setup.Threads,
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(setup.Email.IsRead);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        var evt = Assert.IsType<CorrespondenceThreadStateChangedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(setup.Thread.Id, evt.EmailThreadId);
        Assert.Equal(setup.Thread.CustomerId, evt.CustomerId);
        Assert.Equal(0, evt.UnreadCount);
        Assert.Equal("Active", evt.Status);
        Assert.Equal(actor, evt.ActorUserId);
    }

    [Fact]
    public async Task Handle_MarksUnreadAndPublishesTheNewCount()
    {
        var setup = await NewSetupAsync(read: true);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(setup.TenantId, setup.Email.Id, IsRead: false),
            setup.Emails,
            setup.Threads,
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(setup.Email.IsRead);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        var evt = Assert.IsType<CorrespondenceThreadStateChangedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(1, evt.UnreadCount);
    }

    [Fact]
    public async Task Handle_WhenAlreadyInState_DoesNotPersistNorPublish()
    {
        var setup = await NewSetupAsync(read: true);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(setup.TenantId, setup.Email.Id, IsRead: true),
            setup.Emails,
            setup.Threads,
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_ForAClientNotAssignedToTheUser_ReturnsNotFoundAndChangesNothing()
    {
        var setup = await NewSetupAsync();
        var bus = new FakeMessageBus();

        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(
                setup.TenantId,
                setup.Email.Id,
                IsRead: true,
                RestrictToUserId: Guid.NewGuid()
            ),
            setup.Emails,
            setup.Threads,
            new FakeUnitOfWork(),
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
        Assert.False(setup.Email.IsRead);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_InAMailboxTheUserCannotSee_ReturnsNotFound()
    {
        var setup = await NewSetupAsync();
        setup.Threads.HasVisibleMessageResult = false;

        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(
                setup.TenantId,
                setup.Email.Id,
                IsRead: true,
                VisibleAccountIds: [Guid.NewGuid()]
            ),
            setup.Emails,
            setup.Threads,
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.False(setup.Email.IsRead);
    }

    [Fact]
    public async Task Handle_WithUnknownMessage_ReturnsNotFound()
    {
        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(Guid.NewGuid(), Guid.NewGuid(), IsRead: true),
            new FakeIncomingEmailRepository(),
            new FakeEmailThreadRepository(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithMessageFromAnotherTenant_ReturnsNotFound()
    {
        var setup = await NewSetupAsync();

        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(Guid.NewGuid(), setup.Email.Id, IsRead: true),
            setup.Emails,
            setup.Threads,
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
    }
}
