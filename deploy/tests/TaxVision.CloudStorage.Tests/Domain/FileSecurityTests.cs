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
    public async Task Inspector_detects_tiff_by_the_little_endian_magic_number()
    {
        var inspector = new FileContentInspector();
        byte[] header = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00];
        await using var stream = new MemoryStream(header);

        var result = await inspector.InspectAsync(stream, "scan.tiff", CancellationToken.None);

        Assert.Equal("image/tiff", result.ContentType);
        Assert.True(result.IsSafe);
    }

    [Fact]
    public async Task Inspector_detects_mp4_by_the_ftyp_box()
    {
        var inspector = new FileContentInspector();
        byte[] header = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D];
        await using var stream = new MemoryStream(header);

        var result = await inspector.InspectAsync(stream, "recording.mp4", CancellationToken.None);

        Assert.Equal("video/mp4", result.ContentType);
        Assert.True(result.IsSafe);
    }

    [Fact]
    public async Task Inspector_classifies_a_json_v2_transcript_as_application_json()
    {
        // Transcript JSON v2 del worker (F7): UTF-8 sin BOM, con acentos que cruzan el corte de 512 bytes
        // de la cabecera. Si cayera en text/plain, el scan lo rechazaría por FileTypeCompatibility (.json).
        var segments = string.Join(
            ',',
            Enumerable
                .Range(0, 40)
                .Select(i =>
                    $"{{\"startMs\":{i * 1000},\"endMs\":{i * 1000 + 900},\"speakerUserId\":null,\"speakerName\":\"Begoña Ibáñez\",\"text\":\"Línea {i}: corrección señalada\"}}"
                )
        );
        var json =
            $"{{\"version\":2,\"language\":\"es\",\"model\":\"ggml-small-q5_1\",\"vad\":true,\"durationSeconds\":40,\"speakers\":[],\"segments\":[{segments}]}}";
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var result = await new FileContentInspector().InspectAsync(
            stream,
            "Meeting_Transcript_2026-10-30.json",
            CancellationToken.None
        );

        Assert.Equal("application/json", result.ContentType);
        Assert.True(result.IsSafe);
    }

    [Fact]
    public async Task Inspector_detects_an_audio_only_webm_track_as_video_webm()
    {
        // EBML no distingue audio de video: la política de RecordingTracks acepta video/webm por esto.
        byte[] header = [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01];
        await using var stream = new MemoryStream(header);

        var result = await new FileContentInspector().InspectAsync(stream, "Track_u_t.webm", CancellationToken.None);

        Assert.Equal("video/webm", result.ContentType);
        Assert.True(result.IsSafe);
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
