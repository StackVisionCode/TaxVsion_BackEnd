using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// Metadata de un attachment de un <see cref="IncomingEmail"/>. El binario NUNCA se descarga
/// al recibir el correo (regla dura del servicio) — esta fila solo guarda lo necesario para
/// mostrarlo en la UI hasta que se pide bajo demanda (Fase 8: <see cref="MarkInProgress"/> /
/// <see cref="MarkDownloaded"/> / <see cref="MarkFailed"/>).
/// </summary>
public sealed class IncomingEmailAttachment
{
    public const int FilenameMaxLength = 500;
    public const int ContentTypeMaxLength = 100;
    public const int ProviderAttachmentIdMaxLength = 200;
    public const int FailureReasonMaxLength = 500;

    private IncomingEmailAttachment() { }

    public Guid Id { get; private set; }
    public Guid IncomingEmailId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Filename { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }

    /// <summary>Id opaco que Connectors usa para identificar el binario — Correspondence lo pasa tal cual.</summary>
    public string ProviderAttachmentId { get; private set; } = default!;
    public bool IsInline { get; private set; }
    public AttachmentDownloadStatus DownloadStatus { get; private set; }
    public Guid? CloudStorageFileId { get; private set; }
    public DateTime? DownloadedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Solo <see cref="IncomingEmail.Create"/> construye instancias — mismo motivo que
    /// <see cref="IncomingEmailRecipient.Create"/>: no hay un attachment sin un correo dueño.
    /// </summary>
    internal static IncomingEmailAttachment Create(
        Guid tenantId,
        Guid incomingEmailId,
        string filename,
        string contentType,
        long sizeBytes,
        string providerAttachmentId,
        bool isInline
    )
    {
        return new IncomingEmailAttachment
        {
            Id = Guid.NewGuid(),
            IncomingEmailId = incomingEmailId,
            TenantId = tenantId,
            Filename = filename,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            ProviderAttachmentId = providerAttachmentId,
            IsInline = isInline,
            DownloadStatus = AttachmentDownloadStatus.NotRequested,
            CloudStorageFileId = null,
            DownloadedAtUtc = null,
            FailureReason = null,
        };
    }

    /// <summary>
    /// Arranca la descarga bajo demanda. Solo válido desde <see cref="AttachmentDownloadStatus.NotRequested"/>
    /// o <see cref="AttachmentDownloadStatus.Failed"/> (reintento) — no desde <see cref="AttachmentDownloadStatus.Downloaded"/>
    /// (ya está, ver idempotencia en <c>DownloadAttachmentHandler</c>) ni desde <see cref="AttachmentDownloadStatus.InProgress"/>
    /// (una descarga concurrente ya está en curso, no hay que pisarla).
    /// </summary>
    public Result MarkInProgress()
    {
        if (DownloadStatus is not (AttachmentDownloadStatus.NotRequested or AttachmentDownloadStatus.Failed))
            return Result.Failure(
                new Error(
                    "IncomingEmailAttachment.InvalidTransition",
                    $"Cannot mark as in-progress from status {DownloadStatus}."
                )
            );

        DownloadStatus = AttachmentDownloadStatus.InProgress;
        FailureReason = null;
        return Result.Success();
    }

    /// <summary>Solo válido desde <see cref="AttachmentDownloadStatus.InProgress"/> — el flujo siempre pasa por ahí primero.</summary>
    public Result MarkDownloaded(Guid cloudStorageFileId)
    {
        if (DownloadStatus != AttachmentDownloadStatus.InProgress)
            return Result.Failure(
                new Error(
                    "IncomingEmailAttachment.InvalidTransition",
                    $"Cannot mark as downloaded from status {DownloadStatus}."
                )
            );

        DownloadStatus = AttachmentDownloadStatus.Downloaded;
        CloudStorageFileId = cloudStorageFileId;
        DownloadedAtUtc = DateTime.UtcNow;
        FailureReason = null;
        return Result.Success();
    }

    /// <summary>Solo válido desde <see cref="AttachmentDownloadStatus.InProgress"/> — mismo criterio que <see cref="MarkDownloaded"/>.</summary>
    public Result MarkFailed(string reason)
    {
        if (DownloadStatus != AttachmentDownloadStatus.InProgress)
            return Result.Failure(
                new Error(
                    "IncomingEmailAttachment.InvalidTransition",
                    $"Cannot mark as failed from status {DownloadStatus}."
                )
            );

        DownloadStatus = AttachmentDownloadStatus.Failed;
        FailureReason = reason.Length > FailureReasonMaxLength ? reason[..FailureReasonMaxLength] : reason;
        return Result.Success();
    }
}
