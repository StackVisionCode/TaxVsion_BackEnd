using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
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
            new Ingest.FakeEmailThreadRepository(),
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
    public async Task Handle_WhenCloudStorageHasNotRegisteredTheFreshCopy_ReturnsNotReady()
    {
        var (tenantId, email, repo) = await DownloadedEmailAsync();
        var cloudStorage = new FakeCloudStorageClient { Response = FileNotRegistered() };

        var result = await GetAttachmentDownloadUrlHandler.Handle(
            new GetAttachmentDownloadUrlQuery(tenantId, email.Id, email.Attachments.Single().Id),
            repo,
            new Ingest.FakeEmailThreadRepository(),
            cloudStorage,
            CancellationToken.None
        );

        Assert.Equal("IncomingEmailAttachment.NotReady", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenTheCopyIsStillMissingAfterTheGracePeriod_ReportsTheFailure()
    {
        var (tenantId, email, repo) = await DownloadedEmailAsync();
        var attachment = email.Attachments.Single();
        typeof(IncomingEmailAttachment)
            .GetProperty(nameof(IncomingEmailAttachment.DownloadedAtUtc))!
            .SetValue(
                attachment,
                DateTime.UtcNow - GetAttachmentDownloadUrlHandler.CopyGracePeriod - TimeSpan.FromSeconds(1)
            );
        var cloudStorage = new FakeCloudStorageClient { Response = FileNotRegistered() };

        var result = await GetAttachmentDownloadUrlHandler.Handle(
            new GetAttachmentDownloadUrlQuery(tenantId, email.Id, attachment.Id),
            repo,
            new Ingest.FakeEmailThreadRepository(),
            cloudStorage,
            CancellationToken.None
        );

        Assert.Equal("CloudStorageClient.FileNotRegistered", result.Error.Code);
    }

    private static async Task<(
        Guid TenantId,
        IncomingEmail Email,
        FakeIncomingEmailRepository Repo
    )> DownloadedEmailAsync()
    {
        var tenantId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId);
        var attachment = email.Attachments.Single();
        attachment.MarkInProgress();
        attachment.MarkDownloaded(Guid.NewGuid());
        var repo = new FakeIncomingEmailRepository();
        await repo.AddAsync(email);
        return (tenantId, email, repo);
    }

    private static Result<CloudStorageDownloadUrl> FileNotRegistered() =>
        Result.Failure<CloudStorageDownloadUrl>(new Error("CloudStorageClient.FileNotRegistered", "Not there yet."));

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
            new Ingest.FakeEmailThreadRepository(),
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
            new Ingest.FakeEmailThreadRepository(),
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
            new Ingest.FakeEmailThreadRepository(),
            new FakeCloudStorageClient(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmailAttachment.NotFound", result.Error.Code);
    }

    // Failed es final: error con mensaje para el usuario (no 409, que el CRM sigue esperando) y
    // nunca el FailureReason crudo.
    [Theory]
    [InlineData("EmptyFile", "This attachment is empty and can't be saved.")]
    [InlineData("UploadPolicy", "This file type can't be stored. Ask your admin to allow it, then try again.")]
    [InlineData("StorageQuota.Exceeded", "Your office's storage is full. Free up space and try again.")]
    [InlineData("StorageQuota.Suspended", "Storage is suspended for your office.")]
    [InlineData("SomethingElse", "This attachment couldn't be saved to storage.")]
    public async Task Handle_WithStorageRejectedAttachment_ReturnsUserSafeNonConflictError(
        string rejection,
        string expectedMessage
    )
    {
        var (tenantId, email, repo) = await DownloadedEmailAsync();
        var attachment = email.Attachments.Single();
        attachment.ReleaseRejectedFile(attachment.CloudStorageFileId!.Value, rejection);
        var cloudStorage = new FakeCloudStorageClient();

        var result = await GetAttachmentDownloadUrlHandler.Handle(
            new GetAttachmentDownloadUrlQuery(tenantId, email.Id, attachment.Id),
            repo,
            new Ingest.FakeEmailThreadRepository(),
            cloudStorage,
            CancellationToken.None
        );

        Assert.Equal(AttachmentFailureErrors.StorageRejectedCode, result.Error.Code);
        Assert.Equal(expectedMessage, result.Error.Message);
        Assert.DoesNotContain(attachment.FailureReason!, result.Error.Message);
        Assert.Equal(StatusCodes.Status400BadRequest, result.Error.ToHttpStatusCode());
        Assert.Empty(cloudStorage.Calls);
    }

    [Fact]
    public async Task Handle_WithMailboxDownloadFailure_ReturnsRetryMessageWithoutTheRawReason()
    {
        var tenantId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId);
        var attachment = email.Attachments.Single();
        attachment.MarkInProgress();
        attachment.MarkFailed("GetMessageAttachmentHandler.ProviderFailed: Gmail 500 internal token xyz");
        var repo = new FakeIncomingEmailRepository();
        await repo.AddAsync(email);

        var result = await GetAttachmentDownloadUrlHandler.Handle(
            new GetAttachmentDownloadUrlQuery(tenantId, email.Id, attachment.Id),
            repo,
            new Ingest.FakeEmailThreadRepository(),
            new FakeCloudStorageClient(),
            CancellationToken.None
        );

        Assert.Equal(AttachmentFailureErrors.DownloadFailedCode, result.Error.Code);
        Assert.Equal("The attachment couldn't be downloaded from the mailbox. Try again.", result.Error.Message);
        Assert.DoesNotContain("Gmail", result.Error.Message);
        Assert.Equal(StatusCodes.Status400BadRequest, result.Error.ToHttpStatusCode());
    }
}
