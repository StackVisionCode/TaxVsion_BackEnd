using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;

namespace TaxVision.Correspondence.Tests.Messages;

public sealed class ListMessageAttachmentsHandlerTests
{
    private static IncomingEmail NewIncomingEmail(
        Guid tenantId,
        Guid customerId,
        Guid emailThreadId,
        IReadOnlyCollection<IncomingEmailAttachmentData>? attachments = null
    ) =>
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
                "Subject",
                "Snippet",
                DateTime.UtcNow,
                hasAttachments: attachments is { Count: > 0 },
                attachmentCount: attachments?.Count ?? 0,
                attachments: attachments
            )
            .Value;

    [Fact]
    public async Task Handle_WithThreeAttachments_ReturnsThreeSummariesWithCorrectFields()
    {
        var tenantId = Guid.NewGuid();
        var attachments = new List<IncomingEmailAttachmentData>
        {
            new("invoice.pdf", "application/pdf", 2048, "provider-att-1", false),
            new("logo.png", "image/png", 512, "provider-att-2", true),
            new("contract.docx", "application/vnd.openxmlformats", 4096, "provider-att-3", false),
        };
        var email = NewIncomingEmail(tenantId, Guid.NewGuid(), Guid.NewGuid(), attachments);
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);

        var result = await ListMessageAttachmentsHandler.Handle(
            new ListMessageAttachmentsQuery(tenantId, email.Id),
            incomingEmails,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);

        var expectedByFilename = attachments.ToDictionary(a => a.Filename);
        foreach (var summary in result.Value)
        {
            var expected = expectedByFilename[summary.Filename];
            Assert.Equal(expected.ContentType, summary.ContentType);
            Assert.Equal(expected.SizeBytes, summary.SizeBytes);
            Assert.Equal(expected.IsInline, summary.IsInline);
            Assert.Equal("NotRequested", summary.DownloadStatus);
            Assert.Null(summary.CloudStorageFileId);
            Assert.NotEqual(Guid.Empty, summary.AttachmentId);
        }
    }

    [Fact]
    public async Task Handle_WithNoAttachments_ReturnsEmptyListNotAnError()
    {
        var tenantId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId, Guid.NewGuid(), Guid.NewGuid());
        var incomingEmails = new FakeIncomingEmailRepository();
        await incomingEmails.AddAsync(email);

        var result = await ListMessageAttachmentsHandler.Handle(
            new ListMessageAttachmentsQuery(tenantId, email.Id),
            incomingEmails,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_WithUnknownMessage_ReturnsNotFound()
    {
        var incomingEmails = new FakeIncomingEmailRepository();

        var result = await ListMessageAttachmentsHandler.Handle(
            new ListMessageAttachmentsQuery(Guid.NewGuid(), Guid.NewGuid()),
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

        var result = await ListMessageAttachmentsHandler.Handle(
            new ListMessageAttachmentsQuery(Guid.NewGuid(), email.Id),
            incomingEmails,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
    }
}
