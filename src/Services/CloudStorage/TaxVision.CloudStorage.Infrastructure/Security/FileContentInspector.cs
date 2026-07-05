using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Infrastructure.Security;

public sealed class FileContentInspector : IFileContentInspector
{
    private const int HeaderLength = 512;
    private const int MaxArchiveEntries = 10_000;
    private const long MaxArchiveExpandedBytes = 1024L * 1024 * 1024;
    private const int MaxCompressionRatio = 100;

    public async Task<InspectedContent> InspectAsync(Stream content, string originalName, CancellationToken ct)
    {
        if (!content.CanSeek)
            throw new ArgumentException("Content inspection requires a seekable stream.", nameof(content));

        content.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();
        content.Position = 0;
        var header = new byte[Math.Min(HeaderLength, checked((int)Math.Min(content.Length, HeaderLength)))];
        _ = await content.ReadAsync(header, ct);
        content.Position = 0;

        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        var contentType = DetectContentType(header, extension);
        if (contentType == "application/zip")
        {
            var archiveCheck = InspectArchive(content);
            content.Position = 0;
            if (archiveCheck is not null)
                return new InspectedContent(contentType, hash, false, archiveCheck);
            contentType = OoxmlContentType(extension) ?? contentType;
        }

        return new InspectedContent(contentType, hash);
    }

    private static string DetectContentType(byte[] header, string extension)
    {
        if (StartsWith(header, "%PDF-"u8))
            return "application/pdf";
        if (StartsWith(header, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (StartsWith(header, new byte[] { 0xFF, 0xD8, 0xFF }))
            return "image/jpeg";
        if (StartsWith(header, "GIF87a"u8) || StartsWith(header, "GIF89a"u8))
            return "image/gif";
        if (header.Length >= 12 && StartsWith(header, "RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        if (
            StartsWith(header, new byte[] { 0x50, 0x4B, 0x03, 0x04 })
            || StartsWith(header, new byte[] { 0x50, 0x4B, 0x05, 0x06 })
            || StartsWith(header, new byte[] { 0x50, 0x4B, 0x07, 0x08 })
        )
            return "application/zip";
        if (StartsWith(header, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }))
            return extension switch
            {
                ".doc" => "application/msword",
                ".xls" => "application/vnd.ms-excel",
                ".ppt" => "application/vnd.ms-powerpoint",
                _ => "application/octet-stream",
            };
        if (StartsWith(header, @"{\rtf"u8))
            return "application/rtf";

        if (header.All(value => value is 9 or 10 or 13 || value >= 32))
        {
            var text = Encoding.UTF8.GetString(header).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (extension == ".json" && (text.StartsWith('{') || text.StartsWith('[')))
                return "application/json";
            if (extension == ".xml" && text.StartsWith('<'))
                return "application/xml";
            return extension == ".csv" ? "text/csv" : "text/plain";
        }

        return "application/octet-stream";
    }

    private static string? InspectArchive(Stream content)
    {
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaxArchiveEntries)
                return "Archive contains too many entries.";

            long expanded = 0;
            long compressed = 0;
            foreach (var entry in archive.Entries)
            {
                expanded = checked(expanded + entry.Length);
                compressed = checked(compressed + entry.CompressedLength);
                if (expanded > MaxArchiveExpandedBytes)
                    return "Archive expanded size exceeds the security limit.";
            }

            if (expanded > 0 && compressed > 0 && expanded / compressed > MaxCompressionRatio)
                return "Archive compression ratio exceeds the security limit.";
            return null;
        }
        catch (InvalidDataException)
        {
            return "Archive structure is invalid.";
        }
        catch (OverflowException)
        {
            return "Archive size metadata is invalid.";
        }
    }

    private static string? OoxmlContentType(string extension) =>
        extension switch
        {
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => null,
        };

    private static bool StartsWith(byte[] value, ReadOnlySpan<byte> prefix) => value.AsSpan().StartsWith(prefix);
}
