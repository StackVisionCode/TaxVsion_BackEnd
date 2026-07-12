using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Emailing.Accounts;

/// <summary>Carpeta/mailbox sincronizada de una cuenta. Guarda su cursor de sincronización incremental.</summary>
public sealed class EmailFolder : BaseEntity
{
    private EmailFolder() { }

    public Guid AccountId { get; private set; }
    public string ExternalId { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public EmailFolderKind Kind { get; private set; }

    /// <summary>Cursor incremental del proveedor (historyId de Gmail, deltaToken de Graph, UIDVALIDITY:UID de IMAP).</summary>
    public string? SyncCursor { get; private set; }
    public DateTime? LastSyncAtUtc { get; private set; }
    public int TotalMessages { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static EmailFolder Create(Guid accountId, string externalId, string name, EmailFolderKind kind) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            ExternalId = externalId,
            Name = name,
            Kind = kind,
            CreatedAtUtc = DateTime.UtcNow,
        };

    public void Rename(string name, EmailFolderKind kind)
    {
        Name = name;
        Kind = kind;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UpdateSyncState(string? cursor, int totalMessages)
    {
        SyncCursor = cursor;
        TotalMessages = totalMessages;
        LastSyncAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
