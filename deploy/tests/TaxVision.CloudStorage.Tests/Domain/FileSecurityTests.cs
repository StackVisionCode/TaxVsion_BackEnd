using System.IO.Compression;
using System.Text;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Security;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Tests.Domain;

public sealed class FileSecurityTests
{
    [Theory]
    [InlineData("../other-tenant/file.pdf")]
    [InlineData("tenants/a//file.pdf")]
    [InlineData("/tenants/a/file.pdf")]
    [InlineData(@"tenants\a\file.pdf")]
    public void ObjectKey_rejects_traversal_and_non_canonical_paths(string value)
    {
        var result = ObjectKey.Create(value);
        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.InvalidObjectKey, result.Error);
    }

    [Fact]
    public void Key_builder_scopes_every_object_to_the_tenant_and_uses_an_opaque_file_id()
    {
        var builder = new DefaultObjectKeyBuilder();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var result = builder.Build(
            fileId,
            tenantId,
            OwnerType.Customer,
            Guid.NewGuid(),
            FolderType.Receipts,
            2025,
            "SSN 123-45-6789.pdf"
        );

        Assert.True(result.IsSuccess);
        Assert.StartsWith($"tenants/{tenantId:N}/customer/", result.Value.Value);
        Assert.Contains($"/receipts/2025/{fileId:N}.pdf", result.Value.Value);
        Assert.DoesNotContain("123-45-6789", result.Value.Value);
    }

    [Fact]
    public async Task Inspector_uses_magic_bytes_instead_of_the_claimed_extension()
    {
        var inspector = new FileContentInspector();
        await using var stream = new MemoryStream("%PDF-1.7\ncontent"u8.ToArray());

        var result = await inspector.InspectAsync(stream, "renamed.jpg", CancellationToken.None);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.True(result.IsSafe);
        Assert.Equal(64, result.Sha256.Length);
    }

    [Fact]
    public async Task Inspector_rejects_archive_with_zip_bomb_compression_ratio()
    {
        await using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("payload.txt", CompressionLevel.SmallestSize);
            await using var destination = entry.Open();
            var payload = Encoding.UTF8.GetBytes(new string('A', 2_000_000));
            await destination.WriteAsync(payload);
        }
        archiveStream.Position = 0;

        var result = await new FileContentInspector().InspectAsync(
            archiveStream,
            "payload.zip",
            CancellationToken.None
        );

        Assert.False(result.IsSafe);
        Assert.Contains("compression ratio", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }
}
