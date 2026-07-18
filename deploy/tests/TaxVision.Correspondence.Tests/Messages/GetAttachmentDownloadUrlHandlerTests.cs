using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;

namespace TaxVision.Correspondence.Tests.Messages;

public sealed class GetAttachmentDownloadUrlHandlerTests
{
    private static IncomingEmail NewIncomingEmail(Guid tenantId) =>
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
                hasAttachments: true,
                attachmentCount: 1,
                attachments:
                [
                    new IncomingEmailAttachmentData("invoice.pdf", "application/pdf", 1024, "provider-att-1", false),
                ]
            )
            .Value;

    [Fact]
    public async Task Handle_WithDownloadedAttachment_ReturnsSignedUrl()
    {
        var tenantId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId);
        var attachment = email.Attachments.Single();
        attachment.MarkInProgress();
        var fileId = Guid.NewGuid();
        attachment.MarkDownloaded(fileId);

        var repo = new FakeIncomingEmailRepository();
        await repo.AddAsync(email);
        var cloudStorage = new FakeCloudStorageClient();

        var result = await GetAttachmentDownloadUrlHandler.Handle(
            new GetAttachmentDownloadUrlQuery(tenantId, email.Id, attachment.Id),
            repo,
            cloudStorage,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(attachment.Id, result.Value.AttachmentId);
        Assert.Equal(cloudStorage.Response.Value.DownloadUrl, result.Value.DownloadUrl);

        var call = Assert.Single(cloudStorage.Calls);
        Assert.Equal((tenantId, fileId), call);
    }

    [Fact]
    public async Task Handle_WithNotYetDownloadedAttachment_ReturnsNotReadyWithoutCallingCloudStorage()
    {
        var tenantId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId);
        var attachment = email.Attachments.Single();
        var repo = new FakeIncomingEmailRepository();
        await repo.AddAsync(email);
        var cloudStorage = new FakeCloudStorageClient();

        var result = await GetAttachmentDownloadUrlHandler.Handle(
            new GetAttachmentDownloadUrlQuery(tenantId, email.Id, attachment.Id),
            repo,
            cloudStorage,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmailAttachment.NotReady", result.Error.Code);
        Assert.Empty(cloudStorage.Calls);
    }

    [Fact]
    public async Task Handle_WithUnknownMessage_ReturnsNotFound()
    {
        var repo = new FakeIncomingEmailRepository();

        var result = await GetAttachmentDownloadUrlHandler.Handle(
            new GetAttachmentDownloadUrlQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            repo,
            new FakeCloudStorageClient(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithUnknownAttachment_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId);
        var repo = new FakeIncomingEmailRepository();
        await repo.AddAsync(email);

        var result = await GetAttachmentDownloadUrlHandler.Handle(
            new GetAttachmentDownloadUrlQuery(tenantId, email.Id, Guid.NewGuid()),
            repo,
            new FakeCloudStorageClient(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmailAttachment.NotFound", result.Error.Code);
    }
}
