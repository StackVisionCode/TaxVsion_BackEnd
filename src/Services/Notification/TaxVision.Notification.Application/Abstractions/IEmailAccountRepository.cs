using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Application.Abstractions;

public interface IEmailAccountRepository
{
    Task AddAsync(EmailAccountConnection account, CancellationToken ct = default);

    Task<EmailAccountConnection?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    /// <summary>Cuenta sin filtro de tenant (contexto background: sync worker/consumer).</summary>
    Task<EmailAccountConnection?> GetForSyncAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<EmailAccountConnection>> ListAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Cuentas activas cuya última sincronización es anterior al umbral (para el scheduler).</summary>
    Task<IReadOnlyList<EmailAccountConnection>> GetDueForSyncAsync(
        DateTime olderThanUtc,
        int max,
        CancellationToken ct = default
    );

    // Carpetas
    Task AddFolderAsync(EmailFolder folder, CancellationToken ct = default);
    Task<IReadOnlyList<EmailFolder>> GetFoldersAsync(Guid accountId, CancellationToken ct = default);
    Task<EmailFolder?> GetFolderByExternalIdAsync(Guid accountId, string externalId, CancellationToken ct = default);

    // Mensajes
    Task AddMessageAsync(EmailSyncedMessage message, CancellationToken ct = default);
    Task AddAttachmentAsync(EmailMessageAttachment attachment, CancellationToken ct = default);
    Task<EmailSyncedMessage?> GetMessageByExternalIdAsync(
        Guid accountId,
        string externalMessageId,
        CancellationToken ct = default
    );
    Task<EmailSyncedMessage?> GetMessageAsync(Guid accountId, Guid messageId, CancellationToken ct = default);
    Task<(IReadOnlyList<EmailSyncedMessage> Items, int TotalCount)> GetMessagesAsync(
        Guid accountId,
        Guid? folderId,
        int page,
        int size,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<EmailSyncedMessage>> GetThreadAsync(
        Guid accountId,
        string externalThreadId,
        CancellationToken ct = default
    );

    // Logs
    Task AddSyncLogAsync(EmailSyncLog log, CancellationToken ct = default);
    Task<IReadOnlyList<EmailSyncLog>> GetSyncLogsAsync(Guid accountId, int max, CancellationToken ct = default);
}
