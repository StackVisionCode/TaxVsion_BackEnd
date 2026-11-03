using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Tests.Domain;

public sealed class ProjectionTests
{
    // -------------------- CustomerEmailProjection --------------------

    [Fact]
    public void CustomerEmailProjection_stores_normalized_email_and_display_name()
    {
        var projection = CustomerEmailProjection.ForNewCustomer(
            tenantId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            normalizedEmail: "john@example.com",
            displayName: "John Q. Customer"
        );

        Assert.Equal("john@example.com", projection.NormalizedEmail);
        Assert.Equal("John Q. Customer", projection.DisplayName);
        Assert.False(projection.IsArchived);
    }

    [Fact]
    public void CustomerEmailProjection_change_email_updates_timestamp()
    {
        var projection = CustomerEmailProjection.ForNewCustomer(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "old@example.com",
            "Cust"
        );
        var beforeUpdate = projection.UpdatedAtUtc;
        Thread.Sleep(10);

        projection.ChangeEmail("new@example.com");

        Assert.Equal("new@example.com", projection.NormalizedEmail);
        Assert.True(projection.UpdatedAtUtc > beforeUpdate);
    }

    [Fact]
    public void CustomerEmailProjection_archive_and_reactivate()
    {
        var projection = CustomerEmailProjection.ForNewCustomer(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "x@example.com",
            "Cust"
        );

        projection.MarkArchived();
        Assert.True(projection.IsArchived);

        projection.MarkReactivated();
        Assert.False(projection.IsArchived);
    }

    // -------------------- FileMetadataRef --------------------

    [Fact]
    public void FileMetadataRef_available_factory_stores_checksum()
    {
        var projection = FileMetadataRef.ForAvailable(
            tenantId: Guid.NewGuid(),
            fileId: Guid.NewGuid(),
            objectKey: "tenants/t1/documents/doc.pdf",
            contentType: "application/pdf",
            sizeBytes: 1024,
            checksumSha256: new string('a', 64)
        );

        Assert.Equal(FileScanStatus.Available, projection.Status);
        Assert.Equal(new string('a', 64), projection.ChecksumSha256);
    }

    [Fact]
    public void FileMetadataRef_mark_infected_captures_reason()
    {
        var projection = FileMetadataRef.ForAvailable(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "k",
            "application/pdf",
            10,
            new string('a', 64)
        );

        projection.MarkInfected("EICAR-Test-Signature");

        Assert.Equal(FileScanStatus.Infected, projection.Status);
        Assert.Equal("EICAR-Test-Signature", projection.RejectionReason);
    }

    [Fact]
    public void FileMetadataRef_mark_infected_truncates_long_reports()
    {
        var projection = FileMetadataRef.ForAvailable(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "k",
            "application/pdf",
            10,
            new string('a', 64)
        );
        var longReport = new string('x', 1500);

        projection.MarkInfected(longReport);

        Assert.NotNull(projection.RejectionReason);
        Assert.Equal(1000, projection.RejectionReason!.Length);
    }

    [Fact]
    public void FileMetadataRef_for_rejected_records_a_final_outcome_without_object_metadata()
    {
        var projection = FileMetadataRef.ForRejected(Guid.NewGuid(), Guid.NewGuid(), "StorageQuota.Exceeded");

        Assert.Equal(FileScanStatus.Rejected, projection.Status);
        Assert.True(projection.HasScanOutcome);
        Assert.Equal("StorageQuota.Exceeded", projection.RejectionReason);
        Assert.Equal(string.Empty, projection.ObjectKey);
        Assert.Equal(string.Empty, projection.ContentType);
    }

    [Fact]
    public void FileMetadataRef_for_infected_records_the_scan_report()
    {
        var projection = FileMetadataRef.ForInfected(Guid.NewGuid(), Guid.NewGuid(), "Eicar-Test-Signature");

        Assert.Equal(FileScanStatus.Infected, projection.Status);
        Assert.Equal("Eicar-Test-Signature", projection.RejectionReason);
    }

    [Fact]
    public void FileMetadataRef_mark_rejected_never_downgrades_an_available_file()
    {
        var projection = FileMetadataRef.ForAvailable(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "k",
            "application/pdf",
            10,
            new string('a', 64)
        );

        projection.MarkRejected("late");

        Assert.Equal(FileScanStatus.Available, projection.Status);
        Assert.Null(projection.RejectionReason);
    }

    [Fact]
    public void FileMetadataRef_mark_rejected_keeps_a_more_specific_infected_outcome()
    {
        var projection = FileMetadataRef.ForInfected(Guid.NewGuid(), Guid.NewGuid(), "virus");

        projection.MarkRejected("scan_error");

        Assert.Equal(FileScanStatus.Infected, projection.Status);
        Assert.Equal("virus", projection.RejectionReason);
    }

    [Fact]
    public void FileMetadataRef_rejected_file_becomes_available_on_a_later_rescan()
    {
        var projection = FileMetadataRef.ForRejected(Guid.NewGuid(), Guid.NewGuid(), "scan_error");

        projection.MarkAvailable("k", "application/pdf", 10, new string('a', 64));

        Assert.Equal(FileScanStatus.Available, projection.Status);
        Assert.Null(projection.RejectionReason);
    }
}
