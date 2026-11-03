using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class IncomingEmailAttachmentTests
{
    private static IncomingEmailAttachment NewAttachment()
    {
        var email = IncomingEmail
            .Create(
                Guid.NewGuid(),
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

        return email.Attachments.Single();
    }

    [Fact]
    public void MarkInProgress_from_NotRequested_succeeds()
    {
        var attachment = NewAttachment();

        var result = attachment.MarkInProgress();

        Assert.True(result.IsSuccess);
        Assert.Equal(AttachmentDownloadStatus.InProgress, attachment.DownloadStatus);
    }

    [Fact]
    public void MarkInProgress_from_InProgress_fails()
    {
        var attachment = NewAttachment();
        attachment.MarkInProgress();

        var result = attachment.MarkInProgress();

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmailAttachment.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void MarkInProgress_from_Downloaded_fails()
    {
        var attachment = NewAttachment();
        attachment.MarkInProgress();
        attachment.MarkDownloaded(Guid.NewGuid());

        var result = attachment.MarkInProgress();

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmailAttachment.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void MarkInProgress_from_Failed_succeeds_as_a_retry()
    {
        var attachment = NewAttachment();
        attachment.MarkInProgress();
        attachment.MarkFailed("boom");

        var result = attachment.MarkInProgress();

        Assert.True(result.IsSuccess);
        Assert.Equal(AttachmentDownloadStatus.InProgress, attachment.DownloadStatus);
    }

    [Fact]
    public void MarkDownloaded_from_InProgress_sets_fileId_and_timestamp()
    {
        var attachment = NewAttachment();
        attachment.MarkInProgress();
        var fileId = Guid.NewGuid();

        var result = attachment.MarkDownloaded(fileId);

        Assert.True(result.IsSuccess);
        Assert.Equal(AttachmentDownloadStatus.Downloaded, attachment.DownloadStatus);
        Assert.Equal(fileId, attachment.CloudStorageFileId);
        Assert.NotNull(attachment.DownloadedAtUtc);
        Assert.Null(attachment.FailureReason);
    }

    [Fact]
    public void MarkDownloaded_from_NotRequested_fails()
    {
        var attachment = NewAttachment();

        var result = attachment.MarkDownloaded(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmailAttachment.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void MarkFailed_from_InProgress_sets_reason()
    {
        var attachment = NewAttachment();
        attachment.MarkInProgress();

        var result = attachment.MarkFailed("Connectors timed out");

        Assert.True(result.IsSuccess);
        Assert.Equal(AttachmentDownloadStatus.Failed, attachment.DownloadStatus);
        Assert.Equal("Connectors timed out", attachment.FailureReason);
    }

    [Fact]
    public void MarkFailed_from_NotRequested_fails()
    {
        var attachment = NewAttachment();

        var result = attachment.MarkFailed("boom");

        Assert.True(result.IsFailure);
        Assert.Equal("IncomingEmailAttachment.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void MarkFailed_truncates_reason_to_max_length()
    {
        var attachment = NewAttachment();
        attachment.MarkInProgress();
        var longReason = new string('x', IncomingEmailAttachment.FailureReasonMaxLength + 50);

        attachment.MarkFailed(longReason);

        Assert.Equal(IncomingEmailAttachment.FailureReasonMaxLength, attachment.FailureReason!.Length);
    }

    [Fact]
    public void ReleaseRejectedFile_returns_downloaded_attachment_to_failed_without_file()
    {
        var attachment = NewAttachment();
        var fileId = Guid.NewGuid();
        attachment.MarkInProgress();
        attachment.MarkDownloaded(fileId);

        var released = attachment.ReleaseRejectedFile(fileId, "EmptyFile");

        Assert.True(released);
        Assert.Equal(AttachmentDownloadStatus.Failed, attachment.DownloadStatus);
        Assert.Null(attachment.CloudStorageFileId);
        Assert.Null(attachment.DownloadedAtUtc);
        Assert.Equal("EmptyFile", attachment.StorageRejectionReason);
        Assert.Equal("Storage rejected the file: EmptyFile", attachment.FailureReason);
        Assert.True(attachment.MarkInProgress().IsSuccess);
        Assert.Null(attachment.StorageRejectionReason);
    }

    [Fact]
    public void ReleaseRejectedFile_ignores_a_different_file_and_is_idempotent()
    {
        var attachment = NewAttachment();
        var currentFileId = Guid.NewGuid();
        attachment.MarkInProgress();
        attachment.MarkDownloaded(currentFileId);

        Assert.False(attachment.ReleaseRejectedFile(Guid.NewGuid(), "x"));
        Assert.Equal(AttachmentDownloadStatus.Downloaded, attachment.DownloadStatus);
        Assert.Equal(currentFileId, attachment.CloudStorageFileId);

        Assert.True(attachment.ReleaseRejectedFile(currentFileId, "x"));
        Assert.False(attachment.ReleaseRejectedFile(currentFileId, "x"));
    }

    [Theory]
    [InlineData("EmptyFile", true)]
    [InlineData("UploadPolicy", false)]
    [InlineData("InvalidOwnerOrFolder", true)]
    [InlineData("StorageQuota.Exceeded", false)]
    [InlineData("StorageQuota.Suspended", false)]
    [InlineData("StorageQuota.FileTooLarge", false)]
    public void HasPermanentStorageRejection_depends_on_the_rejection_code(string rejection, bool permanent)
    {
        var attachment = NewAttachment();
        var fileId = Guid.NewGuid();
        attachment.MarkInProgress();
        attachment.MarkDownloaded(fileId);

        attachment.ReleaseRejectedFile(fileId, rejection);

        Assert.Equal(permanent, attachment.HasPermanentStorageRejection);
    }

    [Fact]
    public void Mailbox_failure_after_a_storage_rejection_clears_the_rejection_code()
    {
        var attachment = NewAttachment();
        var fileId = Guid.NewGuid();
        attachment.MarkInProgress();
        attachment.MarkDownloaded(fileId);
        attachment.ReleaseRejectedFile(fileId, "StorageQuota.Exceeded");
        attachment.MarkInProgress();

        attachment.MarkFailed("Connectors timed out");

        Assert.Null(attachment.StorageRejectionReason);
        Assert.False(attachment.HasPermanentStorageRejection);
    }
}
