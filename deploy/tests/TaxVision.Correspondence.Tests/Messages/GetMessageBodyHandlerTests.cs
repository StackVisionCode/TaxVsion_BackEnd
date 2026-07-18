using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Messages;

public sealed class GetMessageBodyHandlerTests
{
    private static IncomingEmail NewIncomingEmail(Guid tenantId, Guid customerId, Guid emailThreadId, Guid accountId) =>
        IncomingEmail
            .Create(
                tenantId,
                customerId,
                emailThreadId,
                accountId,
                "gmail",
                "provider-msg-1",
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
    public async Task Handle_WithKnownMessage_FetchesBodyAndMarksItFetched()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId, Guid.NewGuid(), Guid.NewGuid(), accountId);
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);
        var connectorsClient = new FakeConnectorsClient
        {
            Response = Result.Success(
                new MessageBodyResponse("<p>hi</p>", "hi", new Dictionary<string, string> { ["Subject"] = "Hello" })
            ),
        };
        var unitOfWork = new FakeUnitOfWork();

        var result = await GetMessageBodyHandler.Handle(
            new GetMessageBodyQuery(tenantId, email.Id),
            incomingEmails,
            connectorsClient,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("<p>hi</p>", result.Value.HtmlBody);
        Assert.Equal("hi", result.Value.TextBody);
        Assert.Equal("Hello", result.Value.Headers["Subject"]);
        Assert.Equal(BodyStatus.BodyReady, email.BodyStatus);
        Assert.NotNull(email.BodyFetchedAtUtc);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var call = Assert.Single(connectorsClient.Calls);
        Assert.Equal((tenantId, accountId, "provider-msg-1"), call);
    }

    [Fact]
    public async Task Handle_WithUnknownMessage_ReturnsNotFoundWithoutCallingConnectors()
    {
        var incomingEmails = new FakeIncomingEmailRepository();
        var connectorsClient = new FakeConnectorsClient();
        var unitOfWork = new FakeUnitOfWork();

        var result = await GetMessageBodyHandler.Handle(
            new GetMessageBodyQuery(Guid.NewGuid(), Guid.NewGuid()),
            incomingEmails,
            connectorsClient,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
        Assert.Empty(connectorsClient.Calls);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithMessageFromAnotherTenant_ReturnsNotFound()
    {
        var email = NewIncomingEmail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);

        var result = await GetMessageBodyHandler.Handle(
            new GetMessageBodyQuery(Guid.NewGuid(), email.Id),
            incomingEmails,
            new FakeConnectorsClient(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenConnectorsFails_PropagatesTheErrorAndDoesNotMarkFetched()
    {
        var tenantId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);
        var connectorsClient = new FakeConnectorsClient
        {
            Response = Result.Failure<MessageBodyResponse>(
                new Error("ConnectorsClient.Unavailable", "Connectors did not respond after retrying.")
            ),
        };
        var unitOfWork = new FakeUnitOfWork();

        var result = await GetMessageBodyHandler.Handle(
            new GetMessageBodyQuery(tenantId, email.Id),
            incomingEmails,
            connectorsClient,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ConnectorsClient.Unavailable", result.Error.Code);
        Assert.Equal(BodyStatus.BodyPending, email.BodyStatus);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
