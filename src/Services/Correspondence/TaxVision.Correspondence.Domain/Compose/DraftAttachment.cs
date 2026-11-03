namespace TaxVision.Correspondence.Domain.Compose;

/// <summary>
/// Adjunto de un <see cref="Draft"/> persistido en su propia tabla (<c>DraftAttachments</c>). Hijo sin
/// ciclo de vida propio, igual que <see cref="DraftRecipient"/>: solo <see cref="Draft.AttachFile"/> lo
/// crea. Antes vivía en la columna JSON <c>Drafts.AttachmentsJson</c>; pasó a tabla (D-7) para poder
/// buscar por nombre de archivo con el mismo control de acceso que el resto del correo.
/// </summary>
public sealed class DraftAttachment
{
    private DraftAttachment() { }

    public Guid Id { get; private set; }
    public Guid DraftId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public string Filename { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    internal static DraftAttachment Create(Guid tenantId, Guid draftId, DraftAttachmentRef attachmentRef)
    {
        ArgumentNullException.ThrowIfNull(attachmentRef);

        return new DraftAttachment
        {
            Id = Guid.NewGuid(),
            DraftId = draftId,
            TenantId = tenantId,
            FileId = attachmentRef.FileId,
            Filename = attachmentRef.Filename,
            ContentType = attachmentRef.ContentType,
            SizeBytes = attachmentRef.SizeBytes,
        };
    }

    /// <summary>La referencia inmutable que espera Postmaster al enviar.</summary>
    public DraftAttachmentRef ToRef() => DraftAttachmentRef.Create(FileId, Filename, ContentType, SizeBytes).Value;
}
