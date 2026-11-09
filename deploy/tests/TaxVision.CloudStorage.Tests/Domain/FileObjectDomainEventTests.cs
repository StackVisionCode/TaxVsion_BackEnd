using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Files.Events;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>CST-9. Cada transición anuncia lo suyo, y una transición que falla no anuncia nada.</summary>
public sealed class FileObjectDomainEventTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ChecksumSha256 Checksum = ChecksumSha256.Create(new string('a', 64)).Value;

    private static FileObject Scanning(FolderType folder = FolderType.Documents)
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenant,
                OwnerType.Customer,
                customer,
                folder,
                folder.RequiresYear() ? 2026 : null,
                ObjectKey.Create($"tenants/{tenant:N}/customer/x/{Guid.NewGuid():N}.pdf").Value,
                "form.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                Now,
                Now.AddHours(1)
            )
            .Value;
        Assert.True(file.MarkPendingScan(Now).IsSuccess);
        Assert.True(file.MarkScanning().IsSuccess);
        return file;
    }

    [Fact]
    public void MarkAvailable_carries_what_the_integration_event_needs()
    {
        var file = Scanning();
        var customer = Guid.NewGuid();

        Assert.True(file.MarkAvailable(Checksum, "application/pdf", Now, "corr", customer).IsSuccess);

        var available = Assert.IsType<FileBecameAvailableDomainEvent>(Assert.Single(file.DomainEvents));
        Assert.Equal(file.TenantId, available.TenantId);
        Assert.Equal(file.Id, available.FileId);
        Assert.Equal(file.ObjectKey, available.ObjectKey);
        Assert.Equal("application/pdf", available.ContentType);
        Assert.Equal(new string('a', 64), available.ChecksumSha256);
        Assert.Equal("Customer", available.OwnerType);
        Assert.Equal(file.OwnerId, available.OwnerId);
        Assert.Equal("Documents", available.FolderType);
        Assert.Equal("form.pdf", available.OriginalName);
        Assert.Equal(2026, available.TaxYear);
        Assert.Equal("corr", available.CorrelationId);
        Assert.Equal(customer, available.PortalCustomerId);
    }

    [Theory]
    [InlineData(FolderType.ClientForms)]
    [InlineData(FolderType.Avatars)]
    public void A_form_or_an_avatar_is_not_a_document_the_customer_uploaded(FolderType folder)
    {
        var file = Scanning(folder);

        Assert.True(file.MarkAvailable(Checksum, "application/pdf", Now, "corr", Guid.NewGuid()).IsSuccess);

        Assert.Null(Assert.IsType<FileBecameAvailableDomainEvent>(Assert.Single(file.DomainEvents)).PortalCustomerId);
    }

    [Fact]
    public void Each_quarantine_says_why()
    {
        var infected = Scanning();
        var blocked = Scanning();
        var review = Scanning();

        Assert.True(infected.MarkInfected("Eicar", "application/pdf", Now, "corr").IsSuccess);
        Assert.True(blocked.MarkBlockedByPolicy("Blocked", "application/pdf", Now, "corr").IsSuccess);
        Assert.True(review.MarkPendingReview("Doubt", "application/pdf", Checksum, Now, "corr").IsSuccess);

        Assert.Equal(FileQuarantineKind.Infected, Quarantined(infected).Kind);
        Assert.Equal("Eicar", Quarantined(infected).Reason);
        Assert.Equal(FileQuarantineKind.BlockedByPolicy, Quarantined(blocked).Kind);
        Assert.Equal(FileQuarantineKind.PendingReview, Quarantined(review).Kind);
        Assert.Equal("corr", Quarantined(review).CorrelationId);
    }

    private static FileQuarantinedDomainEvent Quarantined(FileObject file) =>
        Assert.IsType<FileQuarantinedDomainEvent>(Assert.Single(file.DomainEvents));

    [Fact]
    public void Approving_a_review_announces_the_detected_type()
    {
        var file = FileObjectReviewTransitionTests.PendingReview();
        file.ClearDomainEvents();

        Assert.True(file.ApproveReview(Now, "corr").IsSuccess);

        var available = Assert.IsType<FileBecameAvailableDomainEvent>(Assert.Single(file.DomainEvents));
        Assert.Equal("image/jpeg", available.ContentType);
        Assert.Equal("corr", available.CorrelationId);
        Assert.Null(available.PortalCustomerId);
    }

    [Fact]
    public void Rejecting_a_review_is_a_policy_block()
    {
        var file = FileObjectReviewTransitionTests.PendingReview();
        file.ClearDomainEvents();

        Assert.True(file.RejectReview("Blocked after content review.", Now, "corr").IsSuccess);

        Assert.Equal(FileQuarantineKind.BlockedByPolicy, Quarantined(file).Kind);
    }

    [Fact]
    public void A_failed_or_abandoned_scan_keeps_its_stable_reason()
    {
        var failed = Scanning();
        var abandoned = Scanning();

        Assert.True(failed.MarkScanFailed("Mismatch", Now, "content_type_mismatch", "corr").IsSuccess);
        Assert.True(abandoned.MarkScanAbandoned("blob_missing: gone", Now, "blob_missing", "corr").IsSuccess);

        Assert.Equal(
            "content_type_mismatch",
            Assert.IsType<FileScanFailedDomainEvent>(Assert.Single(failed.DomainEvents)).Reason
        );
        Assert.Equal(
            "blob_missing",
            Assert.IsType<FileScanFailedDomainEvent>(Assert.Single(abandoned.DomainEvents)).Reason
        );
    }

    [Fact]
    public void Reinstating_after_a_takedown_does_not_announce_FileAvailable()
    {
        var file = Scanning();
        Assert.True(file.MarkAvailable(Checksum, "application/pdf", Now).IsSuccess);
        Assert.True(file.BlockForTakedown(Now).IsSuccess);
        file.ClearDomainEvents();

        Assert.True(file.ReinstateFromTakedown(Now).IsSuccess);

        Assert.Empty(file.DomainEvents);
    }

    [Fact]
    public void A_transition_that_fails_announces_nothing()
    {
        var file = Scanning();
        Assert.True(file.MarkAvailable(Checksum, "application/pdf", Now).IsSuccess);
        file.ClearDomainEvents();

        Assert.True(file.MarkInfected("Eicar", "application/pdf", Now).IsFailure);
        Assert.True(file.MarkAvailable(Checksum, "application/pdf", Now).IsFailure);
        Assert.True(file.ApproveReview(Now).IsFailure);

        Assert.Empty(file.DomainEvents);
    }
}
