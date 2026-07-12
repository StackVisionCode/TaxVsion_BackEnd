using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Emailing.Accounts;

/// <summary>
/// Referencia a un adjunto de un mensaje sincronizado. Se guarda la metadata; el binario se sube a
/// CloudStorage y su FileId se completa cuando exista el token de servicio (M2M) — pendiente documentado.
/// </summary>
public sealed class EmailMessageAttachment : BaseEntity
{
    private EmailMessageAttachment() { }

    public Guid MessageId { get; private set; }
    public string FileName { get; private set; } = default!;
    public string? ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public string? ExternalAttachmentId { get; private set; }

    /// <summary>FileId en CloudStorage una vez subido el binario (null hasta que se materialice).</summary>
    public Guid? CloudStorageFileId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static EmailMessageAttachment Create(
        Guid messageId,
        string fileName,
        string? contentType,
        long sizeBytes,
        string? externalAttachmentId
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            ExternalAttachmentId = externalAttachmentId,
            CreatedAtUtc = DateTime.UtcNow,
        };

    public void LinkCloudStorage(Guid fileId) => CloudStorageFileId = fileId;
}
