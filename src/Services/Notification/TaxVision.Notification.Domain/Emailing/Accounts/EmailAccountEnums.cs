namespace TaxVision.Notification.Domain.Emailing.Accounts;

/// <summary>Proveedor externo de una cuenta de correo conectada.</summary>
public enum EmailExternalProvider
{
    GmailApi,
    MicrosoftGraph,
    Imap,
    Custom,
}

/// <summary>Tipo de carpeta/mailbox estándar.</summary>
public enum EmailFolderKind
{
    Inbox,
    Sent,
    Drafts,
    Archive,
    Trash,
    Spam,
    Custom,
}

/// <summary>Estado de sincronización de una cuenta.</summary>
public enum AccountSyncStatus
{
    Idle,
    Syncing,
    Error,
    Disconnected,
}

public enum SyncType
{
    Full,
    Incremental,
}

public enum SyncRunStatus
{
    Running,
    Completed,
    Failed,
}
