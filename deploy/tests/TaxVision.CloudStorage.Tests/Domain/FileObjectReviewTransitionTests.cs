using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>Revisión humana de un archivo marcado: solo desde PendingReview y con el checksum guardado.</summary>
public sealed class FileObjectReviewTransitionTests
{
    private const string Sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    internal static FileObject PendingReview(Guid? tenantId = null, FolderType folder = FolderType.Other)
    {
        var tenant = tenantId ?? Guid.NewGuid();
        var now = DateTime.UtcNow;
        var key = ObjectKey.Create($"tenants/{tenant:N}/tenant/{folder.ToSegment()}/{Guid.NewGuid():N}.jpg").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenant,
                OwnerType.Tenant,
                null,
                folder,
                null,
                key,
                "photo.jpg",
                "image/jpeg",
                10,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
        Assert.True(file.MarkPendingScan().IsSuccess);
        Assert.True(file.MarkScanning().IsSuccess);
        Assert.True(
            file.MarkPendingReview(
                "Possible sexual content (score 0.700).",
                "image/jpeg",
                ChecksumSha256.Create(Sha256).Value,
                now
            ).IsSuccess
        );
        return file;
    }

    [Fact]
    public void Pending_review_keeps_the_checksum_so_approval_needs_no_rescan()
    {
        var file = PendingReview();

        Assert.Equal(Sha256, file.ChecksumSha256);
        Assert.True(file.ApproveReview(DateTime.UtcNow).IsSuccess);
        Assert.Equal(FileStatus.Available, file.Status);
    }

    [Fact]
    public void Rejecting_blocks_the_file()
    {
        var file = PendingReview();

        Assert.True(file.RejectReview("Blocked after content review.", DateTime.UtcNow).IsSuccess);
        Assert.Equal(FileStatus.BlockedByPolicy, file.Status);
    }

    [Fact]
    public void A_decided_file_cannot_be_reviewed_again()
    {
        var file = PendingReview();
        Assert.True(file.ApproveReview(DateTime.UtcNow).IsSuccess);

        Assert.Equal(FileErrors.InvalidTransition, file.ApproveReview(DateTime.UtcNow).Error);
        Assert.Equal(FileErrors.InvalidTransition, file.RejectReview("x", DateTime.UtcNow).Error);
    }
}
