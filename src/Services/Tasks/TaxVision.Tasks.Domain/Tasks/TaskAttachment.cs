namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>De dónde salió el adjunto. Enlazar es el caso dominante en una firma fiscal.</summary>
public enum AttachmentOrigin
{
    Linked = 1,
    Uploaded = 2,
    FromTemplate = 3,
}

public enum AttachmentStatus
{
    Pending = 1,
    Available = 2,
    Rejected = 3,
    Detached = 4,
}

/// <summary>
/// La referencia a un archivo de CloudStorage. Task guarda el id opaco y nunca el byte: el archivo
/// vive en CloudStorage y sigue ahí aunque el adjunto se desadjunte.
/// </summary>
/// <remarks>
/// Entidad hija: sólo la crean y la mutan los métodos de <see cref="TaskItem"/>. El <see cref="Id"/>
/// lo genera el dominio, así que su config EF necesita <c>ValueGeneratedNever()</c>.
/// </remarks>
public sealed class TaskAttachment
{
    public Guid Id { get; }
    public Guid TaskId { get; }
    public Guid TenantId { get; }

    public Guid FileId { get; }
    public string DisplayName { get; } = string.Empty;
    public string? ContentType { get; }
    public long SizeBytes { get; }

    public AttachmentOrigin Origin { get; }
    public AttachmentStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }

    public Guid AttachedByUserId { get; }
    public DateTime AttachedAtUtc { get; }
    public DateTime? DetachedAtUtc { get; private set; }

    public bool IsActive => Status != AttachmentStatus.Detached;

    private TaskAttachment() { }

    private TaskAttachment(
        Guid taskId,
        Guid tenantId,
        Guid fileId,
        string displayName,
        string? contentType,
        long sizeBytes,
        AttachmentOrigin origin,
        AttachmentStatus status,
        Guid attachedByUserId,
        DateTime nowUtc
    )
    {
        Id = Guid.NewGuid();
        TaskId = taskId;
        TenantId = tenantId;
        FileId = fileId;
        DisplayName = displayName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Origin = origin;
        Status = status;
        AttachedByUserId = attachedByUserId;
        AttachedAtUtc = nowUtc;
    }

    /// <summary>
    /// Nace <c>Available</c>: el archivo ya está escaneado. Esperar un <c>FileAvailable</c> que
    /// ocurrió hace tres semanas lo dejaría en <c>Pending</c> para siempre —CloudStorage no
    /// republica el evento de un archivo viejo—.
    /// </summary>
    internal static TaskAttachment Link(
        Guid taskId,
        Guid tenantId,
        Guid fileId,
        string displayName,
        string? contentType,
        long sizeBytes,
        Guid byUserId,
        DateTime nowUtc
    ) =>
        new(
            taskId,
            tenantId,
            fileId,
            displayName,
            contentType,
            sizeBytes,
            AttachmentOrigin.Linked,
            AttachmentStatus.Available,
            byUserId,
            nowUtc
        );

    /// <summary>
    /// Copia de referencia del guion: mismo <c>fileId</c> que las demás instancias de la plantilla.
    /// </summary>
    internal static TaskAttachment FromTemplate(
        Guid taskId,
        Guid tenantId,
        Guid fileId,
        string displayName,
        string? contentType,
        long sizeBytes,
        Guid byUserId,
        DateTime nowUtc
    ) =>
        new(
            taskId,
            tenantId,
            fileId,
            displayName,
            contentType,
            sizeBytes,
            AttachmentOrigin.FromTemplate,
            AttachmentStatus.Available,
            byUserId,
            nowUtc
        );

    /// <summary>Recién subido: falta que CloudStorage lo escanee.</summary>
    internal static TaskAttachment Upload(
        Guid taskId,
        Guid tenantId,
        Guid fileId,
        string displayName,
        string? contentType,
        long sizeBytes,
        Guid byUserId,
        DateTime nowUtc
    ) =>
        new(
            taskId,
            tenantId,
            fileId,
            displayName,
            contentType,
            sizeBytes,
            AttachmentOrigin.Uploaded,
            AttachmentStatus.Pending,
            byUserId,
            nowUtc
        );

    /// <summary>Idempotente: el evento de CloudStorage puede llegar dos veces.</summary>
    internal bool MarkAvailable()
    {
        if (Status != AttachmentStatus.Pending)
            return false;

        Status = AttachmentStatus.Available;
        return true;
    }

    internal bool MarkRejected(string reason, DateTime nowUtc)
    {
        if (Status is AttachmentStatus.Rejected or AttachmentStatus.Detached)
            return false;

        Status = AttachmentStatus.Rejected;
        RejectionReason = reason;
        DetachedAtUtc = nowUtc;
        return true;
    }

    internal bool Detach(DateTime nowUtc)
    {
        if (Status == AttachmentStatus.Detached)
            return false;

        Status = AttachmentStatus.Detached;
        DetachedAtUtc = nowUtc;
        return true;
    }
}
