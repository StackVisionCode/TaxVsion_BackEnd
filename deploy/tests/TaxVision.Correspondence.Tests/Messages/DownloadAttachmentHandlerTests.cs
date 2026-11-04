using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Messages;

public sealed class DownloadAttachmentHandlerTests
{
    private static IncomingEmail NewIncomingEmail(
        Guid tenantId,
        Guid customerId,
        Guid accountId,
        DateTime receivedAtUtc
    ) =>
        IncomingEmail
            .Create(
                tenantId,
                customerId,
                Guid.NewGuid(),
                accountId,
                "gmail",
                "provider-msg-1",
                EmailAddress.Create("customer@example.com").Value,
                null,
                "Subject",
                "Snippet",
                receivedAtUtc,
                hasAttachments: true,
                attachmentCount: 1,
                attachments:
                [
                    new IncomingEmailAttachmentData(
                        "invoice.pdf",
                        "application/pdf",
                        1024,
                        "provider-att-1",
                        false,
                        "1"
                    ),
                ]
            )
            .Value;

    private static async Task<(
        IncomingEmail Email,
        FakeIncomingEmailRepository Repo,
        Ingest.FakeEmailThreadRepository Threads,
        FakeConnectorsClient Connectors,
        FakeTempBucketUploader Uploader,
        FakeMessageBus Bus,
        FakeCorrelationContext Correlation,
        FakeUnitOfWork UnitOfWork
    )> SetupAsync(Guid tenantId, Guid customerId, Guid accountId, DateTime receivedAtUtc)
    {
        var email = NewIncomingEmail(tenantId, customerId, accountId, receivedAtUtc);
        var repo = new FakeIncomingEmailRepository();
        await repo.AddAsync(email);
        return (
            email,
            repo,
            new Ingest.FakeEmailThreadRepository(),
            new FakeConnectorsClient(),
            new FakeTempBucketUploader(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork()
        );
    }

    [Fact]
    public async Task Handle_WithNotRequestedAttachment_FetchesUploadsPublishesAndMarksDownloaded()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var receivedAt = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var (email, repo, threads, connectors, uploader, bus, correlation, uow) = await SetupAsync(
            tenantId,
            customerId,
            accountId,
            receivedAt
        );
        var attachment = email.Attachments.Single();

        var result = await DownloadAttachmentHandler.Handle(
            new DownloadAttachmentCommand(tenantId, email.Id, attachment.Id, actorId),
            repo,
            threads,
            connectors,
            uploader,
            bus,
            correlation,
            uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(AttachmentDownloadStatus.Downloaded.ToString(), result.Value.DownloadStatus);
        Assert.Equal(attachment.CloudStorageFileId, result.Value.CloudStorageFileId);
        Assert.Equal(AttachmentDownloadStatus.Downloaded, attachment.DownloadStatus);
        Assert.NotNull(attachment.CloudStorageFileId);

        var call = Assert.Single(connectors.AttachmentCalls);
        // partId + filename/size viajan como selector estable (el providerAttachmentId de Gmail es efímero).
        Assert.Equal((tenantId, accountId, "provider-msg-1", "provider-att-1", "invoice.pdf", 1024L, "1"), call);

        var uploadCall = Assert.Single(uploader.Calls);
        Assert.Equal("invoice.pdf", uploadCall.Filename);
        Assert.Equal("application/pdf", uploadCall.ContentType);

        var published = Assert.Single(bus.Published);
        var evt = Assert.IsType<SaveFileRequestedIntegrationEvent>(published);
        Assert.Equal(tenantId, evt.TenantId);
        Assert.Equal(attachment.CloudStorageFileId, evt.FileId);
        Assert.Equal("correspondence", evt.RequestingService);
        Assert.Equal(actorId, evt.ActorId);
        Assert.Equal("Customer", evt.OwnerType);
        Assert.Equal(customerId, evt.OwnerId);
        Assert.Equal("EmailIncoming", evt.FolderType);
        Assert.Equal(2026, evt.TaxYear);
        Assert.Equal("invoice.pdf", evt.OriginalName);
        Assert.Equal("application/pdf", evt.ContentType);

        // MarkInProgress + MarkDownloaded — 2 saves (idempotency checkpoint + final commit).
        Assert.Equal(2, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithAlreadyDownloadedAttachment_ShortCircuitsWithoutCallingConnectors()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var (email, repo, threads, connectors, uploader, bus, correlation, uow) = await SetupAsync(
            tenantId,
            Guid.NewGuid(),
            accountId,
            DateTime.UtcNow
        );
        var attachment = email.Attachments.Single();
        attachment.MarkInProgress();
        var existingFileId = Guid.NewGuid();
        attachment.MarkDownloaded(existingFileId);

        var result = await DownloadAttachmentHandler.Handle(
            new DownloadAttachmentCommand(tenantId, email.Id, attachment.Id, actorId),
            repo,
            threads,
            connectors,
            uploader,
            bus,
            correlation,
            uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(existingFileId, result.Value.CloudStorageFileId);
        Assert.Empty(connectors.AttachmentCalls);
        Assert.Empty(uploader.Calls);
        Assert.Empty(bus.Published);
        Assert.Equal(0, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WhenConnectorsFetchFails_MarksFailedAndReturnsError()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var (email, repo, threads, connectors, uploader, bus, correlation, uow) = await SetupAsync(
            tenantId,
            Guid.NewGuid(),
            accountId,
            DateTime.UtcNow
        );
        var attachment = email.Attachments.Single();
        connectors.AttachmentResponse = Result.Failure<ConnectorsAttachmentBytes>(
            new Error("GetMessageAttachmentHandler.ProviderFailed", "Provider unavailable.")
        );

        var result = await DownloadAttachmentHandler.Handle(
            new DownloadAttachmentCommand(tenantId, email.Id, attachment.Id, actorId),
            repo,
            threads,
            connectors,
            uploader,
            bus,
            correlation,
            uow,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("GetMessageAttachmentHandler.ProviderFailed", result.Error.Code);
        Assert.Equal(AttachmentDownloadStatus.Failed, attachment.DownloadStatus);
        Assert.Contains("GetMessageAttachmentHandler.ProviderFailed", attachment.FailureReason);
        Assert.Empty(uploader.Calls);
        Assert.Empty(bus.Published);
        Assert.Equal(2, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WhenUploadFails_MarksFailedAndReturnsError()
    {
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var (email, repo, threads, connectors, uploader, bus, correlation, uow) = await SetupAsync(
            tenantId,
            Guid.NewGuid(),
            accountId,
            DateTime.UtcNow
        );
        var attachment = email.Attachments.Single();
        uploader.Response = Result.Failure<TempBucketUploadResult>(
            new Error("CorrespondenceTempBucketUploader.UploadFailed", "MinIO PUT failed.")
        );

        var result = await DownloadAttachmentHandler.Handle(
            new DownloadAttachmentCommand(tenantId, email.Id, attachment.Id, actorId),
            repo,
            threads,
            connectors,
            uploader,
            bus,
            correlation,
            uow,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("CorrespondenceTempBucketUploader.UploadFailed", result.Error.Code);
        Assert.Equal(AttachmentDownloadStatus.Failed, attachment.DownloadStatus);
        Assert.Empty(bus.Published);
        Assert.Equal(2, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithUnknownMessage_ReturnsNotFound()
    {
        var repo = new FakeIncomingEmailRepository();

        var result = await DownloadAttachmentHandler.Handle(
            new DownloadAttachmentCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            repo,
            new Ingest.FakeEmailThreadRepository(),
            new FakeConnectorsClient(),
            new FakeTempBucketUploader(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmail.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithUnknownAttachment_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var (email, repo, threads, connectors, uploader, bus, correlation, uow) = await SetupAsync(
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        var result = await DownloadAttachmentHandler.Handle(
            new DownloadAttachmentCommand(tenantId, email.Id, Guid.NewGuid(), Guid.NewGuid()),
            repo,
            threads,
            connectors,
            uploader,
            bus,
            correlation,
            uow,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmailAttachment.NotFound", result.Error.Code);
    }

    // Rechazo permanente de storage: no se vuelve a bajar ni copiar, se devuelve el mismo mensaje.
    [Theory]
    [InlineData("EmptyFile", "This attachment is empty and can't be saved.")]
    [InlineData("InvalidOwnerOrFolder", "This attachment couldn't be saved to storage.")]
    public async Task Handle_AfterPermanentStorageRejection_ReturnsUserSafeErrorWithoutRetrying(
        string rejection,
        string expectedMessage
    )
    {
        var tenantId = Guid.NewGuid();
        var (email, repo, threads, connectors, uploader, bus, correlation, uow) = await SetupAsync(
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        var attachment = email.Attachments.Single();
        var fileId = Guid.NewGuid();
        attachment.MarkInProgress();
        attachment.MarkDownloaded(fileId);
        attachment.ReleaseRejectedFile(fileId, rejection);

        var result = await DownloadAttachmentHandler.Handle(
            new DownloadAttachmentCommand(tenantId, email.Id, attachment.Id, Guid.NewGuid()),
            repo,
            threads,
            connectors,
            uploader,
            bus,
            correlation,
            uow,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(AttachmentFailureErrors.StorageRejectedCode, result.Error.Code);
        Assert.Equal(expectedMessage, result.Error.Message);
        Assert.NotEqual(StatusCodes.Status409Conflict, result.Error.ToHttpStatusCode());
        Assert.Empty(connectors.AttachmentCalls);
        Assert.Empty(uploader.Calls);
        Assert.Empty(bus.Published);
        Assert.Equal(0, uow.SaveChangesCallCount);
        Assert.Equal(AttachmentDownloadStatus.Failed, attachment.DownloadStatus);
    }

    // Cuota, suspensión o política de subida pueden liberarse: el reintento vuelve a bajar y a pedir la copia.
    [Theory]
    [InlineData("StorageQuota.Exceeded")]
    [InlineData("StorageQuota.Suspended")]
    [InlineData("UploadPolicy")]
    public async Task Handle_AfterTransientStorageRejection_RetriesTheDownload(string rejection)
    {
        var tenantId = Guid.NewGuid();
        var (email, repo, threads, connectors, uploader, bus, correlation, uow) = await SetupAsync(
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        var attachment = email.Attachments.Single();
        var rejectedFileId = Guid.NewGuid();
        attachment.MarkInProgress();
        attachment.MarkDownloaded(rejectedFileId);
        attachment.ReleaseRejectedFile(rejectedFileId, rejection);

        var result = await DownloadAttachmentHandler.Handle(
            new DownloadAttachmentCommand(tenantId, email.Id, attachment.Id, Guid.NewGuid()),
            repo,
            threads,
            connectors,
            uploader,
            bus,
            correlation,
            uow,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(connectors.AttachmentCalls);
        var evt = Assert.IsType<SaveFileRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.NotEqual(rejectedFileId, evt.FileId);
        Assert.Equal(AttachmentDownloadStatus.Downloaded, attachment.DownloadStatus);
        Assert.Null(attachment.StorageRejectionReason);
    }
}
