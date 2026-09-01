using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Messages;

public sealed class SetMessageReadStateHandlerTests
{
    private static IncomingEmail NewEmail(Guid tenantId) =>
        IncomingEmail
            .Create(
                tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
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

    [Fact]
    public async Task Handle_MarksReadAndPersists()
    {
        var tenantId = Guid.NewGuid();
        var email = NewEmail(tenantId);
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(tenantId, email.Id, IsRead: true),
            incomingEmails,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(email.IsRead);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_MarksUnreadAndPersists()
    {
        var tenantId = Guid.NewGuid();
        var email = NewEmail(tenantId);
        email.MarkRead(DateTime.UtcNow);
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(tenantId, email.Id, IsRead: false),
            incomingEmails,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(email.IsRead);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WhenAlreadyInState_DoesNotPersist()
    {
        var tenantId = Guid.NewGuid();
        var email = NewEmail(tenantId);
        email.MarkRead(DateTime.UtcNow);
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(tenantId, email.Id, IsRead: true),
            incomingEmails,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithUnknownMessage_ReturnsNotFound()
    {
        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(Guid.NewGuid(), Guid.NewGuid(), IsRead: true),
            new FakeIncomingEmailRepository(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithMessageFromAnotherTenant_ReturnsNotFound()
    {
        var email = NewEmail(Guid.NewGuid());
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);

        var result = await SetMessageReadStateHandler.Handle(
            new SetMessageReadStateCommand(Guid.NewGuid(), email.Id, IsRead: true),
            incomingEmails,
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
    }
}
