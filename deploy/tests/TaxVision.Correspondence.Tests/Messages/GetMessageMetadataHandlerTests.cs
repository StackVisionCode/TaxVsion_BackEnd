using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;

namespace TaxVision.Correspondence.Tests.Messages;

public sealed class GetMessageMetadataHandlerTests
{
    private static IncomingEmail NewIncomingEmail(Guid tenantId, Guid customerId, Guid emailThreadId) =>
        IncomingEmail
            .Create(
                tenantId,
                customerId,
                emailThreadId,
                Guid.NewGuid(),
                "gmail",
                "provider-msg-1",
                EmailAddress.Create("customer@example.com").Value,
                "The Customer",
                "Subject line",
                "A short snippet",
                DateTime.UtcNow,
                hasAttachments: true,
                attachmentCount: 2
            )
            .Value;

    [Fact]
    public async Task Handle_WithKnownMessage_ReturnsMetadataWithoutBody()
    {
        var tenantId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);

        var result = await GetMessageMetadataHandler.Handle(
            new GetMessageMetadataQuery(tenantId, email.Id),
            incomingEmails,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var summary = result.Value;
        Assert.Equal(email.Id, summary.MessageId);
        Assert.Equal(MessageDirection.Inbound, summary.Direction);
        Assert.Equal(email.From, summary.From);
        Assert.Equal(email.FromDisplayName, summary.FromDisplayName);
        Assert.Equal("Subject line", summary.Subject);
        Assert.Equal("A short snippet", summary.Snippet);
        Assert.Null(summary.ToAddresses);
        Assert.Equal(email.ReceivedAtUtc, summary.OccurredAtUtc);
        Assert.True(summary.HasAttachments);
        Assert.Equal(2, summary.AttachmentCount);
        Assert.Equal("BodyPending", summary.BodyStatus);

        var properties = typeof(MessageSummary).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("HtmlBody", properties);
        Assert.DoesNotContain("TextBody", properties);
    }

    [Fact]
    public async Task Handle_WithUnknownMessage_ReturnsNotFound()
    {
        var incomingEmails = new FakeIncomingEmailRepository();

        var result = await GetMessageMetadataHandler.Handle(
            new GetMessageMetadataQuery(Guid.NewGuid(), Guid.NewGuid()),
            incomingEmails,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithMessageFromAnotherTenant_ReturnsNotFound()
    {
        var email = NewIncomingEmail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);

        var result = await GetMessageMetadataHandler.Handle(
            new GetMessageMetadataQuery(Guid.NewGuid(), email.Id),
            incomingEmails,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
    }
}
