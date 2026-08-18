using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ClientRequests;

/// <summary>
/// Un archivo que el cliente subió para responder a un pedido. Igual que en las tareas, aquí sólo
/// vive el id: el byte es de CloudStorage.
/// </summary>
/// <remarks>
/// Reutiliza <see cref="AttachmentStatus"/> en vez de estrenar un enum propio: es exactamente el
/// mismo ciclo —espera el escaneo, se acepta o se rechaza, o lo borran de origen— y duplicarlo con
/// otro nombre invitaría a que los dos se desincronicen.
/// </remarks>
public sealed class ClientRequestDocument
{
    public Guid Id { get; }
    public Guid ClientRequestId { get; private set; }

    public Guid FileId { get; }
    public string DisplayName { get; } = string.Empty;
    public string? ContentType { get; }
    public long SizeBytes { get; }

    public AttachmentStatus Status { get; private set; }

    /// <summary>
    /// El motivo real, para el preparador. Al cliente se le dice que vuelva a subirlo y nada más:
    /// «tu archivo tiene un virus» no le sirve para actuar y regala información de la infraestructura.
    /// </summary>
    public string? RejectionReason { get; private set; }

    public DateTime UploadedAtUtc { get; }
    public DateTime? ResolvedAtUtc { get; private set; }

    public bool IsActive => Status != AttachmentStatus.Detached;

    private ClientRequestDocument() { }

    private ClientRequestDocument(Guid fileId, string displayName, string? contentType, long sizeBytes, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        FileId = fileId;
        DisplayName = displayName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Status = AttachmentStatus.Pending;
        UploadedAtUtc = nowUtc;
    }

    /// <summary>
    /// Nace <c>Pending</c> siempre —incluso si CloudStorage ya lo escaneó—: viene de fuera de la
    /// firma y el veredicto lo confirma el escaneo, no quien lo sube.
    /// </summary>
    internal static ClientRequestDocument Upload(
        Guid fileId,
        string displayName,
        string? contentType,
        long sizeBytes,
        DateTime nowUtc
    ) => new(fileId, displayName, contentType, sizeBytes, nowUtc);

    internal void AttachTo(Guid clientRequestId) => ClientRequestId = clientRequestId;

    internal bool MarkAvailable(DateTime nowUtc)
    {
        if (Status != AttachmentStatus.Pending)
            return false;

        Status = AttachmentStatus.Available;
        ResolvedAtUtc = nowUtc;
        return true;
    }

    internal bool MarkRejected(string reason, DateTime nowUtc)
    {
        if (Status is AttachmentStatus.Rejected or AttachmentStatus.Detached)
            return false;

        Status = AttachmentStatus.Rejected;
        RejectionReason = reason;
        ResolvedAtUtc = nowUtc;
        return true;
    }

    internal bool MarkDetached(DateTime nowUtc)
    {
        if (Status == AttachmentStatus.Detached)
            return false;

        Status = AttachmentStatus.Detached;
        ResolvedAtUtc = nowUtc;
        return true;
    }
}
