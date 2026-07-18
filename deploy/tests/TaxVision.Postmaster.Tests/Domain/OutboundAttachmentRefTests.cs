using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class OutboundAttachmentRefTests
{
    [Fact]
    public void Create_succeeds_with_valid_data()
    {
        var fileId = Guid.NewGuid();

        var result = OutboundAttachmentRef.Create(fileId, "invoice.pdf", "application/pdf", 2048);

        Assert.True(result.IsSuccess);
        Assert.Equal(fileId, result.Value.CloudStorageFileId);
        Assert.Equal("invoice.pdf", result.Value.Filename);
        Assert.Equal("application/pdf", result.Value.ContentType);
        Assert.Equal(2048, result.Value.SizeBytes);
    }

    [Fact]
    public void Create_accepts_a_35MB_attachment_without_a_domain_level_cap()
    {
        var result = OutboundAttachmentRef.Create(Guid.NewGuid(), "big.zip", "application/zip", 35L * 1024 * 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_rejects_empty_cloud_storage_file_id()
    {
        var result = OutboundAttachmentRef.Create(Guid.Empty, "invoice.pdf", "application/pdf", 2048);

        Assert.True(result.IsFailure);
        Assert.Equal("OutboundAttachmentRef.CloudStorageFileId", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_empty_filename()
    {
        var result = OutboundAttachmentRef.Create(Guid.NewGuid(), "", "application/pdf", 2048);

        Assert.True(result.IsFailure);
        Assert.Equal("OutboundAttachmentRef.Filename", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_filename_longer_than_255_chars()
    {
        var filename = new string('f', 256) + ".pdf";

        var result = OutboundAttachmentRef.Create(Guid.NewGuid(), filename, "application/pdf", 2048);

        Assert.True(result.IsFailure);
        Assert.Equal("OutboundAttachmentRef.Filename", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_empty_content_type()
    {
        var result = OutboundAttachmentRef.Create(Guid.NewGuid(), "invoice.pdf", "", 2048);

        Assert.True(result.IsFailure);
        Assert.Equal("OutboundAttachmentRef.ContentType", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_zero_or_negative_size_bytes()
    {
        var result = OutboundAttachmentRef.Create(Guid.NewGuid(), "invoice.pdf", "application/pdf", 0);

        Assert.True(result.IsFailure);
        Assert.Equal("OutboundAttachmentRef.SizeBytes", result.Error.Code);
    }
}
