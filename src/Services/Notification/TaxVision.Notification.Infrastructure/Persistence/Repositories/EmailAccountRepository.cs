using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class EmailAccountRepository(NotificationDbContext db) : IEmailAccountRepository
{
    public async Task AddAsync(EmailAccountConnection account, CancellationToken ct = default) =>
        await db.EmailAccountConnections.AddAsync(account, ct);

    public async Task<EmailAccountConnection?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        await db.EmailAccountConnections.FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);

    public async Task<EmailAccountConnection?> GetForSyncAsync(Guid id, CancellationToken ct = default) =>
        await db.EmailAccountConnections.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<EmailAccountConnection>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        await db
            .EmailAccountConnections.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EmailAccountConnection>> GetDueForSyncAsync(
        DateTime olderThanUtc,
        int max,
        CancellationToken ct = default
    ) =>
        await db
            .EmailAccountConnections.Where(a =>
                a.IsActive
                && a.SyncStatus != AccountSyncStatus.Syncing
                && (a.LastSyncAtUtc == null || a.LastSyncAtUtc < olderThanUtc)
            )
            .OrderBy(a => a.LastSyncAtUtc)
            .Take(max)
            .ToListAsync(ct);

    public async Task AddFolderAsync(EmailFolder folder, CancellationToken ct = default) =>
        await db.EmailFolders.AddAsync(folder, ct);

    public async Task<IReadOnlyList<EmailFolder>> GetFoldersAsync(Guid accountId, CancellationToken ct = default) =>
        await db.EmailFolders.AsNoTracking().Where(f => f.AccountId == accountId).OrderBy(f => f.Name).ToListAsync(ct);

    public async Task<EmailFolder?> GetFolderByExternalIdAsync(
        Guid accountId,
        string externalId,
        CancellationToken ct = default
    ) => await db.EmailFolders.FirstOrDefaultAsync(f => f.AccountId == accountId && f.ExternalId == externalId, ct);

    public async Task AddMessageAsync(EmailSyncedMessage message, CancellationToken ct = default) =>
        await db.EmailSyncedMessages.AddAsync(message, ct);

    public async Task AddAttachmentAsync(EmailMessageAttachment attachment, CancellationToken ct = default) =>
        await db.EmailMessageAttachments.AddAsync(attachment, ct);

    public async Task<EmailSyncedMessage?> GetMessageByExternalIdAsync(
        Guid accountId,
        string externalMessageId,
        CancellationToken ct = default
    ) =>
        await db.EmailSyncedMessages.FirstOrDefaultAsync(
            m => m.AccountId == accountId && m.ExternalMessageId == externalMessageId,
            ct
        );

    public async Task<EmailSyncedMessage?> GetMessageAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken ct = default
    ) =>
        await db
            .EmailSyncedMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.AccountId == accountId && m.Id == messageId, ct);

    public async Task<(IReadOnlyList<EmailSyncedMessage> Items, int TotalCount)> GetMessagesAsync(
        Guid accountId,
        Guid? folderId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db.EmailSyncedMessages.AsNoTracking().Where(m => m.AccountId == accountId);
        if (folderId is not null)
            query = query.Where(m => m.FolderId == folderId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(m => m.ReceivedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<EmailSyncedMessage>> GetThreadAsync(
        Guid accountId,
        string externalThreadId,
        CancellationToken ct = default
    ) =>
        await db
            .EmailSyncedMessages.AsNoTracking()
            .Where(m => m.AccountId == accountId && m.ExternalThreadId == externalThreadId)
            .OrderBy(m => m.ReceivedAtUtc)
            .ToListAsync(ct);

    public async Task AddSyncLogAsync(EmailSyncLog log, CancellationToken ct = default) =>
        await db.EmailSyncLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<EmailSyncLog>> GetSyncLogsAsync(
        Guid accountId,
        int max,
        CancellationToken ct = default
    ) =>
        await db
            .EmailSyncLogs.AsNoTracking()
            .Where(l => l.AccountId == accountId)
            .OrderByDescending(l => l.StartedAtUtc)
            .Take(max)
            .ToListAsync(ct);
}
