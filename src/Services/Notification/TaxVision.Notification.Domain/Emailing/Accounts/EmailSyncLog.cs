using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Emailing.Accounts;

/// <summary>Registro de una ejecución de sincronización de una cuenta (auditoría y diagnóstico).</summary>
public sealed class EmailSyncLog : BaseEntity
{
    private EmailSyncLog() { }

    public Guid AccountId { get; private set; }
    public SyncType Type { get; private set; }
    public SyncRunStatus Status { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public int FoldersSynced { get; private set; }
    public int MessagesSynced { get; private set; }
    public string? Error { get; private set; }

    public static EmailSyncLog Start(Guid accountId, SyncType type) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            Status = SyncRunStatus.Running,
            StartedAtUtc = DateTime.UtcNow,
        };

    public void Complete(int foldersSynced, int messagesSynced)
    {
        Status = SyncRunStatus.Completed;
        FoldersSynced = foldersSynced;
        MessagesSynced = messagesSynced;
        FinishedAtUtc = DateTime.UtcNow;
    }

    public void Fail(string error, int foldersSynced, int messagesSynced)
    {
        Status = SyncRunStatus.Failed;
        Error = error is { Length: > 1024 } ? error[..1024] : error;
        FoldersSynced = foldersSynced;
        MessagesSynced = messagesSynced;
        FinishedAtUtc = DateTime.UtcNow;
    }
}
