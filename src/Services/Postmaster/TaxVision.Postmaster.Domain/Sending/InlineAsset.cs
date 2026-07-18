using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>
/// Referencia a una imagen embebida por Content-ID (ej: logo del tenant) que
/// <c>MimeMessageBuilder</c> (Infrastructure) resuelve a un <c>LinkedResource</c> — evita que
/// Outlook bloquee imágenes remotas al abrir el email. El byte real vive en CloudStorage; este VO
/// solo referencia el archivo y garantiza el límite individual de tamaño.
/// </summary>
public sealed class InlineAsset
{
    public const long MaxSizeBytes = 200 * 1024;

    private InlineAsset() { }

    public string ContentId { get; private set; } = default!;
    public Guid CloudStorageFileId { get; private set; }
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    public static Result<InlineAsset> Create(
        string contentId,
        Guid cloudStorageFileId,
        string contentType,
        long sizeBytes
    )
    {
        if (string.IsNullOrWhiteSpace(contentId) || contentId.Length > 100)
            return Result.Failure<InlineAsset>(
                new Error("InlineAsset.ContentId", "ContentId is required and must be at most 100 chars.")
            );

        if (cloudStorageFileId == Guid.Empty)
            return Result.Failure<InlineAsset>(
                new Error("InlineAsset.CloudStorageFileId", "CloudStorageFileId is required.")
            );

        if (string.IsNullOrWhiteSpace(contentType))
            return Result.Failure<InlineAsset>(new Error("InlineAsset.ContentType", "ContentType is required."));

        if (sizeBytes <= 0 || sizeBytes > MaxSizeBytes)
            return Result.Failure<InlineAsset>(
                new Error("InlineAsset.SizeBytes", $"SizeBytes must be between 1 and {MaxSizeBytes} bytes.")
            );

        return Result.Success(
            new InlineAsset
            {
                ContentId = contentId.Trim(),
                CloudStorageFileId = cloudStorageFileId,
                ContentType = contentType.Trim(),
                SizeBytes = sizeBytes,
            }
        );
    }
}
