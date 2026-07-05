using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Emailing.Accounts;

/// <summary>
/// Mensaje sincronizado del buzón del tenant. A diferencia de NotificationLog, SÍ guarda el cuerpo:
/// es contenido del propio buzón del tenant, aislado por cuenta/tenant. Los adjuntos van a CloudStorage
/// (referencia por FileId) — la subida de bytes es un pendiente documentado.
/// </summary>
public sealed class EmailSyncedMessage : BaseEntity
{
    private EmailSyncedMessage() { }

    public Guid AccountId { get; private set; }
    public Guid FolderId { get; private set; }
    public string ExternalMessageId { get; private set; } = default!;
    public string? ExternalThreadId { get; private set; }

    public string? Subject { get; private set; }
    public string? FromAddress { get; private set; }
    public string ToJson { get; private set; } = "[]";
    public string CcJson { get; private set; } = "[]";
    public string BccJson { get; private set; } = "[]";
    public string? Snippet { get; private set; }
    public string? BodyHtml { get; private set; }
    public string? BodyText { get; private set; }
    public string? HeadersJson { get; private set; }

    public bool IsRead { get; private set; }
    public bool IsStarred { get; private set; }
    public bool HasAttachments { get; private set; }
    public DateTime? ReceivedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static EmailSyncedMessage Create(
        Guid accountId,
        Guid folderId,
        string externalMessageId,
        string? externalThreadId,
        string? subject,
        string? fromAddress,
        string toJson,
        string ccJson,
        string bccJson,
        string? snippet,
        string? bodyHtml,
        string? bodyText,
        string? headersJson,
        bool isRead,
        bool isStarred,
        bool hasAttachments,
        DateTime? receivedAtUtc,
        DateTime? sentAtUtc
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            FolderId = folderId,
            ExternalMessageId = externalMessageId,
            ExternalThreadId = externalThreadId,
            Subject = Trim(subject, 500),
            FromAddress = Trim(fromAddress, 320),
            ToJson = toJson,
            CcJson = ccJson,
            BccJson = bccJson,
            Snippet = Trim(snippet, 512),
            BodyHtml = bodyHtml,
            BodyText = bodyText,
            HeadersJson = headersJson,
            IsRead = isRead,
            IsStarred = isStarred,
            HasAttachments = hasAttachments,
            ReceivedAtUtc = receivedAtUtc,
            SentAtUtc = sentAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
        };

    /// <summary>Actualiza flags mutables en una re-sincronización (leído/destacado).</summary>
    public void UpdateFlags(bool isRead, bool isStarred)
    {
        IsRead = isRead;
        IsStarred = isStarred;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string? Trim(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..max];
}
