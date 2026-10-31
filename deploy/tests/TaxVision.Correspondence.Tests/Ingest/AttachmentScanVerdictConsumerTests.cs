using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Ingest;

public sealed class AttachmentScanVerdictConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static IncomingEmail EmailWithDownloadedAttachment(Guid fileId)
    {
        var email = IncomingEmail
            .Create(
                Tenant,
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
                attachments: [new IncomingEmailAttachmentData("bad.pdf", "application/pdf", 100, "prov-att", false)]
            )
            .Value;
        var attachment = email.Attachments.First();
        attachment.MarkInProgress();
        attachment.MarkDownloaded(fileId);
        return email;
    }

    // T-20: en revisión no se ofrece; aprobado (FileAvailable de EmailIncoming) vuelve a descargable.
    [Fact]
    public async Task Pending_review_holds_the_attachment_and_approval_restores_it()
    {
        var fileId = Guid.NewGuid();
        var email = EmailWithDownloadedAttachment(fileId);
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(email);
        var attachment = email.Attachments.First();

        await AttachmentScanVerdictConsumer.Handle(
            new FilePendingReviewIntegrationEvent { TenantId = Tenant, FileId = fileId, ObjectKey = "k", Reason = "x" },
            incoming,
            new FakeUnitOfWork(),
            NullLogger<IncomingEmailAttachment>.Instance,
            CancellationToken.None
        );
        Assert.Equal(AttachmentDownloadStatus.UnderReview, attachment.DownloadStatus);
        Assert.Equal(fileId, attachment.CloudStorageFileId);

        var available = new FileAvailableIntegrationEvent
        {
            TenantId = Tenant,
            FileId = fileId,
            ObjectKey = "k",
            ContentType = "image/jpeg",
            SizeBytes = 100,
            ChecksumSha256 = "abc",
            CreatedBy = Guid.NewGuid(),
            FolderType = "Documents",
        };
        await AttachmentScanVerdictConsumer.Handle(available, incoming, new FakeUnitOfWork(), NullLogger<IncomingEmailAttachment>.Instance, CancellationToken.None);
        Assert.Equal(AttachmentDownloadStatus.UnderReview, attachment.DownloadStatus);

        await AttachmentScanVerdictConsumer.Handle(
            available with { FolderType = "EmailIncoming" },
            incoming,
            new FakeUnitOfWork(),
            NullLogger<IncomingEmailAttachment>.Instance,
            CancellationToken.None
        );
        Assert.Equal(AttachmentDownloadStatus.Downloaded, attachment.DownloadStatus);
    }

    [Fact]
    public async Task Infected_event_blocks_the_matching_attachment()
    {
        var fileId = Guid.NewGuid();
        var email = EmailWithDownloadedAttachment(fileId);
        var incoming = new FakeIncomingEmailRepository();
        await incoming.AddAsync(email);
        var uow = new FakeUnitOfWork();

        await AttachmentScanVerdictConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                TenantId = Tenant,
                FileId = fileId,
                ObjectKey = "k",
                ScanReport = "EICAR",
            },
            incoming,
            uow,
            NullLogger<IncomingEmailAttachment>.Instance,
            CancellationToken.None
        );

        Assert.Equal(AttachmentDownloadStatus.Blocked, email.Attachments.First().DownloadStatus);
    }

    [Fact]
    public async Task Verdict_for_an_unknown_file_is_a_no_op()
    {
        var incoming = new FakeIncomingEmailRepository();
        var uow = new FakeUnitOfWork();

        await AttachmentScanVerdictConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                TenantId = Tenant,
                FileId = Guid.NewGuid(),
                ObjectKey = "k",
                PolicyReason = "x",
                CreatedBy = Guid.NewGuid(),
            },
            incoming,
            uow,
            NullLogger<IncomingEmailAttachment>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, uow.SaveChangesCallCount);
    }
}
