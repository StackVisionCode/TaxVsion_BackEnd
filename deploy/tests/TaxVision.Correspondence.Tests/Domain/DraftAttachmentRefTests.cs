using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class DraftAttachmentRefTests
{
    [Fact]
    public void Create_succeeds_with_valid_data()
    {
        var fileId = Guid.NewGuid();

        var result = DraftAttachmentRef.Create(fileId, "invoice.pdf", "application/pdf", 2048);

        Assert.True(result.IsSuccess);
        Assert.Equal(fileId, result.Value.FileId);
        Assert.Equal("invoice.pdf", result.Value.Filename);
        Assert.Equal("application/pdf", result.Value.ContentType);
        Assert.Equal(2048, result.Value.SizeBytes);
    }

    [Fact]
    public void Create_fails_when_fileId_is_empty()
    {
        var result = DraftAttachmentRef.Create(Guid.Empty, "invoice.pdf", "application/pdf", 2048);

        Assert.True(result.IsFailure);
        Assert.Equal("DraftAttachmentRef.FileId", result.Error.Code);
    }

    [Fact]
    public void Create_fails_when_filename_is_blank()
    {
        var result = DraftAttachmentRef.Create(Guid.NewGuid(), "   ", "application/pdf", 2048);

        Assert.True(result.IsFailure);
        Assert.Equal("DraftAttachmentRef.Filename", result.Error.Code);
    }

    [Fact]
    public void Create_fails_when_contentType_is_blank()
    {
        var result = DraftAttachmentRef.Create(Guid.NewGuid(), "invoice.pdf", "   ", 2048);

        Assert.True(result.IsFailure);
        Assert.Equal("DraftAttachmentRef.ContentType", result.Error.Code);
    }

    [Fact]
    public void Create_fails_when_sizeBytes_is_negative()
    {
        var result = DraftAttachmentRef.Create(Guid.NewGuid(), "invoice.pdf", "application/pdf", -1);

        Assert.True(result.IsFailure);
        Assert.Equal("DraftAttachmentRef.SizeBytes", result.Error.Code);
    }

    [Fact]
    public void Create_succeeds_with_zero_sizeBytes()
    {
        var result = DraftAttachmentRef.Create(Guid.NewGuid(), "empty.txt", "text/plain", 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.SizeBytes);
    }

    /// <summary>
    /// No hay cap de tamaño en esta capa a propósito (plan §16) — el límite real lo aplica el
    /// proveedor resuelto río abajo al momento de enviar (Gmail 35MB, Graph 3MB, SMTP manual
    /// configurable). Un valor absurdamente grande igual debe pasar acá.
    /// </summary>
    [Fact]
    public void Create_succeeds_with_a_huge_sizeBytes_because_there_is_no_cap_at_this_layer()
    {
        const long farBeyondAnyRealProviderLimit = 50L * 1024 * 1024 * 1024; // 50 GB

        var result = DraftAttachmentRef.Create(
            Guid.NewGuid(),
            "huge-file.bin",
            "application/octet-stream",
            farBeyondAnyRealProviderLimit
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(farBeyondAnyRealProviderLimit, result.Value.SizeBytes);
    }
}
