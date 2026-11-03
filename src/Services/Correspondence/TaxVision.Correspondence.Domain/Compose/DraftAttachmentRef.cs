using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Domain.Compose;

/// <summary>
/// Referencia a un archivo ya subido a CloudStorage — nunca bytes locales. Mirror 1:1 de
/// <c>OutboundAttachmentRef</c> (Postmaster, D3 Compose §modelo), incluyendo la ausencia
/// deliberada de un cap de tamaño: el límite real lo aplica el proveedor resuelto río abajo al
/// momento de enviar (Gmail 35MB, Graph 3MB, SMTP manual configurable) — nunca esta capa. Es el
/// valor validado que entra por <see cref="Draft.AttachFile"/> y el que sale hacia Postmaster; lo
/// persistido es <see cref="DraftAttachment"/> (tabla <c>DraftAttachments</c>, D-7).
/// </summary>
public sealed class DraftAttachmentRef
{
    public const int FilenameMaxLength = 500;
    public const int ContentTypeMaxLength = 100;

    private DraftAttachmentRef() { }

    public Guid FileId { get; private set; }
    public string Filename { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    public static Result<DraftAttachmentRef> Create(Guid fileId, string filename, string contentType, long sizeBytes)
    {
        if (fileId == Guid.Empty)
            return Result.Failure<DraftAttachmentRef>(new Error("DraftAttachmentRef.FileId", "FileId is required."));

        if (string.IsNullOrWhiteSpace(filename) || filename.Length > FilenameMaxLength)
            return Result.Failure<DraftAttachmentRef>(
                new Error(
                    "DraftAttachmentRef.Filename",
                    $"Filename is required and must be at most {FilenameMaxLength} chars."
                )
            );

        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > ContentTypeMaxLength)
            return Result.Failure<DraftAttachmentRef>(
                new Error(
                    "DraftAttachmentRef.ContentType",
                    $"ContentType is required and must be at most {ContentTypeMaxLength} chars."
                )
            );

        if (sizeBytes < 0)
            return Result.Failure<DraftAttachmentRef>(
                new Error("DraftAttachmentRef.SizeBytes", "SizeBytes must be zero or greater.")
            );

        return Result.Success(
            new DraftAttachmentRef
            {
                FileId = fileId,
                Filename = filename.Trim(),
                ContentType = contentType.Trim(),
                SizeBytes = sizeBytes,
            }
        );
    }
}
