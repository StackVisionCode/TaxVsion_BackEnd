namespace TaxVision.Correspondence.Application.Search;

/// <summary>
/// Respuesta de <c>GET /correspondence/search</c>: correos, archivos y personas que el usuario puede ver; un grupo
/// no pedido viene vacío. Los clientes no salen de aquí: el front los busca en su directorio de clientes en paralelo.
/// </summary>
public sealed record MailSearchResult(
    IReadOnlyList<EmailSearchHit> Emails,
    IReadOnlyList<FileSearchHit> Files,
    IReadOnlyList<PersonSearchHit> People
);

/// <summary>
/// Un corresponsal: remitente de entrantes y/o destinatario de salientes visibles, distinto por dirección (en
/// minúsculas). <see cref="MessageCount"/> y <see cref="LastContactAtUtc"/> sobre los correos que pasan los filtros;
/// <see cref="DisplayName"/> y <see cref="CustomerId"/> = los más recientes que haya. <see cref="Direction"/>:
/// <c>From</c> (solo nos escribió), <c>To</c> (solo le escribimos) o <c>Both</c>.
/// </summary>
public sealed record PersonSearchHit(
    string Address,
    string? DisplayName,
    Guid? CustomerId,
    int MessageCount,
    DateTime LastContactAtUtc,
    string Direction
);

/// <summary>
/// Un correo que coincide. <see cref="Source"/>: <c>Thread</c> (conversación de Inbox/Archived; una fila por
/// conversación), <c>Incoming</c> (entrante en la papelera) u <c>Outgoing</c> (enviado o borrador).
/// <see cref="MessageId"/> es el entrante que coincidió o el id del borrador/enviado; null si coincidió solo
/// el asunto de la conversación. <see cref="ThreadId"/> es null para un enviado nuevo sin conversación.
/// <see cref="MatchedField"/>: <c>Subject</c>, <c>From</c>, <c>Snippet</c> o <c>To</c>.
/// </summary>
public sealed record EmailSearchHit(
    string Source,
    string Folder,
    Guid? ThreadId,
    Guid? MessageId,
    Guid? CustomerId,
    string Subject,
    string MatchedField,
    DateTime OccurredAtUtc,
    string? FromAddress,
    string? FromName,
    IReadOnlyList<string> ToAddresses,
    string? Snippet,
    bool HasAttachments,
    // Conversación: algo sin leer; entrante: su leído; enviado/borrador: siempre false.
    bool IsUnread = false,
    // Solo enviados/borradores: copia y copia oculta.
    IReadOnlyList<string>? CcAddresses = null,
    IReadOnlyList<string>? BccAddresses = null
);

/// <summary>
/// Un adjunto cuyo nombre coincide. <see cref="Source"/>: <c>Incoming</c> (adjunto de un entrante; se
/// descarga por <c>/messages/{messageId}/attachments/{attachmentId}</c>) u <c>Outgoing</c> (adjunto de un
/// enviado; <see cref="FileId"/> es el archivo en CloudStorage). <see cref="FileId"/> de un entrante es null
/// hasta que alguien lo descarga.
/// </summary>
public sealed record FileSearchHit(
    string Source,
    Guid AttachmentId,
    Guid MessageId,
    Guid? ThreadId,
    Guid? CustomerId,
    string Filename,
    string ContentType,
    long SizeBytes,
    DateTime OccurredAtUtc,
    string? FromAddress,
    string? FromName,
    IReadOnlyList<string> ToAddresses,
    Guid? FileId,
    string? DownloadStatus
);

/// <summary>Valores estables de <see cref="EmailSearchHit"/> y <see cref="FileSearchHit"/> (contrato con el front).</summary>
public static class MailSearchValues
{
    public const string SourceThread = "Thread";
    public const string SourceIncoming = "Incoming";
    public const string SourceOutgoing = "Outgoing";

    public const string FolderInbox = "Inbox";
    public const string FolderArchived = "Archived";
    public const string FolderSent = "Sent";
    public const string FolderDrafts = "Drafts";
    public const string FolderTrash = "Trash";

    public const string MatchedSubject = "Subject";
    public const string MatchedFrom = "From";
    public const string MatchedSnippet = "Snippet";
    public const string MatchedTo = "To";

    public const string DirectionFrom = "From";
    public const string DirectionTo = "To";
    public const string DirectionBoth = "Both";
}
