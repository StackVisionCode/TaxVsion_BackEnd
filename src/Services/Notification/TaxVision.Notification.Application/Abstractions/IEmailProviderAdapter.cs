using BuildingBlocks.Results;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Application.Abstractions;

public sealed record ProviderFolder(string ExternalId, string Name, EmailFolderKind Kind);

public sealed record ProviderAttachment(
    string FileName,
    string? ContentType,
    long SizeBytes,
    string? ExternalId,
    byte[]? Content = null
);

public sealed record ProviderMessage(
    string ExternalMessageId,
    string? ExternalThreadId,
    string? Subject,
    string? FromAddress,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string? Snippet,
    string? BodyHtml,
    string? BodyText,
    string? HeadersJson,
    bool IsRead,
    bool IsStarred,
    DateTime? ReceivedAtUtc,
    DateTime? SentAtUtc,
    IReadOnlyList<ProviderAttachment> Attachments
);

/// <summary>Resultado de sincronizar una carpeta: mensajes nuevos/actualizados + cursor incremental.</summary>
public sealed record ProviderFolderSync(IReadOnlyList<ProviderMessage> Messages, string? NewCursor, int TotalMessages);

/// <summary>
/// Adaptador de un proveedor de correo externo (Gmail API, Microsoft Graph, IMAP, custom). La
/// implementación descifra las credenciales de la cuenta internamente (no viajan en claro por la app).
/// </summary>
public interface IEmailProviderAdapter
{
    EmailExternalProvider Provider { get; }

    Task<Result<IReadOnlyList<ProviderFolder>>> ListFoldersAsync(EmailAccountConnection account, CancellationToken ct = default);

    Task<Result<ProviderFolderSync>> SyncFolderAsync(
        EmailAccountConnection account,
        ProviderFolder folder,
        string? cursor,
        bool full,
        int maxMessages,
        CancellationToken ct = default
    );
}

/// <summary>Resuelve el adaptador correspondiente al proveedor de una cuenta.</summary>
public interface IEmailProviderAdapterFactory
{
    IEmailProviderAdapter Resolve(EmailExternalProvider provider);
}
