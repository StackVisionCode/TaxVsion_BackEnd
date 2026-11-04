using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Messages;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Messages;

public sealed class AttachmentSaveRejectedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static IncomingEmail EmailWithDownloadedAttachment(Guid tenantId, Guid fileId)
    {
        var email = IncomingEmail
            .Create(
                tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Gmail",
                Guid.NewGuid().ToString(),
                EmailAddress.Create("client@example.com").Value,
                null,
                "Subject",
                "snippet",
                DateTime.UtcNow,
                true,
                1,
                attachments: [new IncomingEmailAttachmentData("empty.pdf", "application/pdf", 100, "prov-att", false)]
            )
            .Value;
        var attachment = email.Attachments.First();
        attachment.MarkInProgress();
        attachment.MarkDownloaded(fileId);
        return email;
    }

    private static FileSaveRejectedIntegrationEvent Rejected(
        Guid fileId,
        string requestingService = "correspondence"
    ) =>
        new()
        {
            TenantId = Tenant,
            FileId = fileId,
            RequestingService = requestingService,
            OwnerType = "Customer",
            OwnerId = Guid.NewGuid(),
            FolderType = "EmailIncoming",
            Reason = "EmptyFile",
        };

    private static Task HandleAsync(
        FileSaveRejectedIntegrationEvent evt,
        FakeIncomingEmailRepository incoming,
        FakeUnitOfWork uow
    ) =>
        AttachmentSaveRejectedConsumer.Handle(
            evt,
            incoming,
            uow,
            NullLogger<IncomingEmailAttachment>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Own_rejection_returns_the_attachment_to_failed_so_it_can_be_retried()
    {
        var fileId = Guid.NewGuid();
        var email = EmailWithDownloadedAttachment(Tenant, fileId);
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(email);
        var uow = new FakeUnitOfWork();

        await HandleAsync(Rejected(fileId), incoming, uow);

        var attachment = email.Attachments.First();
        Assert.Equal(AttachmentDownloadStatus.Failed, attachment.DownloadStatus);
        Assert.Null(attachment.CloudStorageFileId);
        Assert.Contains("EmptyFile", attachment.FailureReason);
        Assert.Equal(1, uow.SaveChangesCallCount);
        Assert.True(attachment.MarkInProgress().IsSuccess);
    }

    [Fact]
    public async Task Redelivered_rejection_is_a_no_op()
    {
        var fileId = Guid.NewGuid();
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(EmailWithDownloadedAttachment(Tenant, fileId));
        var uow = new FakeUnitOfWork();

        await HandleAsync(Rejected(fileId), incoming, uow);
        await HandleAsync(Rejected(fileId), incoming, uow);

        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejection_of_an_older_file_leaves_the_current_file_untouched()
    {
        var currentFileId = Guid.NewGuid();
        var email = EmailWithDownloadedAttachment(Tenant, currentFileId);
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(email);
        var uow = new FakeUnitOfWork();

        await HandleAsync(Rejected(Guid.NewGuid()), incoming, uow);

        var attachment = email.Attachments.First();
        Assert.Equal(AttachmentDownloadStatus.Downloaded, attachment.DownloadStatus);
        Assert.Equal(currentFileId, attachment.CloudStorageFileId);
        Assert.Equal(0, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejection_requested_by_another_service_is_ignored()
    {
        var fileId = Guid.NewGuid();
        var email = EmailWithDownloadedAttachment(Tenant, fileId);
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(email);
        var uow = new FakeUnitOfWork();

        await HandleAsync(Rejected(fileId, requestingService: "signature"), incoming, uow);

        Assert.Equal(AttachmentDownloadStatus.Downloaded, email.Attachments.First().DownloadStatus);
        Assert.Equal(fileId, email.Attachments.First().CloudStorageFileId);
        Assert.Equal(0, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejection_from_another_tenant_is_ignored()
    {
        var fileId = Guid.NewGuid();
        var email = EmailWithDownloadedAttachment(Guid.NewGuid(), fileId);
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(email);
        var uow = new FakeUnitOfWork();

        await HandleAsync(Rejected(fileId), incoming, uow);

        Assert.Equal(AttachmentDownloadStatus.Downloaded, email.Attachments.First().DownloadStatus);
        Assert.Equal(0, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejection_of_an_unknown_file_is_a_no_op()
    {
        var incoming = new FakeIncomingEmailRepository();
        var uow = new FakeUnitOfWork();

        await HandleAsync(Rejected(Guid.NewGuid()), incoming, uow);

        Assert.Equal(0, uow.SaveChangesCallCount);
    }
}
