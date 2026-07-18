using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Domain.Sending;

/// <summary>
/// Referencia a un adjunto ya subido a CloudStorage por el preparador (Correspondence <c>Draft</c>) —
/// nunca bytes, mismo criterio que <see cref="InlineAsset"/> (D3 Compose §11.3/§12). Postmaster recién
/// pide los bytes reales a CloudStorage al momento de enviar (<c>IOutboundAttachmentFetcher</c>, D3
/// Compose Fase 4); este VO solo referencia el archivo para audit y para que Connectors sepa qué
/// adjuntar. A diferencia de <see cref="InlineAsset"/>, sin cap de tamaño acá — el cap real es el del
/// proveedor resuelto en el momento del envío (35MB Gmail, 3MB Graph, configurable SMTP manual).
/// </summary>
public sealed class OutboundAttachmentRef
{
    private OutboundAttachmentRef() { }

    public Guid CloudStorageFileId { get; private set; }
    public string Filename { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    public static Result<OutboundAttachmentRef> Create(
        Guid cloudStorageFileId,
        string filename,
        string contentType,
        long sizeBytes
    )
    {
        if (cloudStorageFileId == Guid.Empty)
            return Result.Failure<OutboundAttachmentRef>(
                new Error("OutboundAttachmentRef.CloudStorageFileId", "CloudStorageFileId is required.")
            );

        if (string.IsNullOrWhiteSpace(filename) || filename.Length > 255)
            return Result.Failure<OutboundAttachmentRef>(
                new Error("OutboundAttachmentRef.Filename", "Filename is required and must be at most 255 chars.")
            );

        if (string.IsNullOrWhiteSpace(contentType))
            return Result.Failure<OutboundAttachmentRef>(
                new Error("OutboundAttachmentRef.ContentType", "ContentType is required.")
            );

        if (sizeBytes <= 0)
            return Result.Failure<OutboundAttachmentRef>(
                new Error("OutboundAttachmentRef.SizeBytes", "SizeBytes must be greater than zero.")
            );

        return Result.Success(
            new OutboundAttachmentRef
            {
                CloudStorageFileId = cloudStorageFileId,
                Filename = filename.Trim(),
                ContentType = contentType.Trim(),
                SizeBytes = sizeBytes,
            }
        );
    }
}
