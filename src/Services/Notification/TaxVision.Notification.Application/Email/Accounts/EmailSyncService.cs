using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Accounts;
using Wolverine;

namespace TaxVision.Notification.Application.Email.Accounts;

/// <summary>
/// Orquesta la sincronización de una cuenta: resuelve el adaptador, upserta carpetas y mensajes (por
/// MessageId externo), avanza cursores, escribe el log y publica el resultado. Fuera del request HTTP.
/// </summary>
public sealed class EmailSyncService(
    IEmailAccountRepository accounts,
    IEmailProviderAdapterFactory adapters,
    ICloudStorageClient cloudStorage,
    IMessageBus bus,
    ICorrelationContext correlation,
    IUnitOfWork unitOfWork,
    ILogger<EmailSyncService> logger
) : IEmailSyncService
{
    private const int MaxMessagesPerFolder = 100;

    public async Task<Result> SyncAccountAsync(Guid accountId, SyncType type, CancellationToken ct = default)
    {
        var account = await accounts.GetForSyncAsync(accountId, ct);
        if (account is null)
            return Result.Failure(new Error("EmailAccount.NotFound", "Account not found."));
        if (!account.IsActive)
            return Result.Success();

        var started = account.MarkSyncStarted();
        if (started.IsFailure)
            return started;

        var log = EmailSyncLog.Start(accountId, type);
        await accounts.AddSyncLogAsync(log, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var adapter = adapters.Resolve(account.Provider);
        var full = type == SyncType.Full;

        var foldersResult = await adapter.ListFoldersAsync(account, ct);
        if (foldersResult.IsFailure)
            return await FailAsync(account, log, foldersResult.Error.Message, 0, 0, ct);

        var foldersSynced = 0;
        var messagesSynced = 0;

        foreach (var providerFolder in foldersResult.Value)
        {
            var folder = await accounts.GetFolderByExternalIdAsync(account.Id, providerFolder.ExternalId, ct);
            if (folder is null)
            {
                folder = EmailFolder.Create(
                    account.Id,
                    providerFolder.ExternalId,
                    providerFolder.Name,
                    providerFolder.Kind
                );
                await accounts.AddFolderAsync(folder, ct);
            }
            else
            {
                folder.Rename(providerFolder.Name, providerFolder.Kind);
            }

            var syncResult = await adapter.SyncFolderAsync(
                account,
                providerFolder,
                full ? null : folder.SyncCursor,
                full,
                MaxMessagesPerFolder,
                ct
            );
            if (syncResult.IsFailure)
            {
                logger.LogWarning(
                    "Folder {Folder} sync failed: {Error}",
                    providerFolder.Name,
                    syncResult.Error.Message
                );
                continue;
            }

            foreach (var pm in syncResult.Value.Messages)
            {
                var existing = await accounts.GetMessageByExternalIdAsync(account.Id, pm.ExternalMessageId, ct);
                if (existing is not null)
                {
                    existing.UpdateFlags(pm.IsRead, pm.IsStarred);
                    continue;
                }

                var message = EmailSyncedMessage.Create(
                    account.Id,
                    folder.Id,
                    pm.ExternalMessageId,
                    pm.ExternalThreadId,
                    pm.Subject,
                    pm.FromAddress,
                    JsonSerializer.Serialize(pm.To),
                    JsonSerializer.Serialize(pm.Cc),
                    JsonSerializer.Serialize(pm.Bcc),
                    pm.Snippet,
                    pm.BodyHtml,
                    pm.BodyText,
                    pm.HeadersJson,
                    pm.IsRead,
                    pm.IsStarred,
                    pm.Attachments.Count > 0,
                    pm.ReceivedAtUtc,
                    pm.SentAtUtc
                );
                await accounts.AddMessageAsync(message, ct);

                foreach (var att in pm.Attachments)
                {
                    var attachment = EmailMessageAttachment.Create(
                        message.Id,
                        att.FileName,
                        att.ContentType,
                        att.SizeBytes,
                        att.ExternalId
                    );

                    // Sube el binario a CloudStorage con token de servicio (M2M) del tenant. Si el tipo no
                    // esta permitido o falla, se conserva solo la metadata (FileId queda null).
                    if (att.Content is { Length: > 0 })
                    {
                        var upload = await cloudStorage.UploadAsync(
                            new CloudStorageUpload(
                                att.Content,
                                att.FileName,
                                att.ContentType ?? "application/octet-stream",
                                "Communication",
                                message.Id,
                                "EmailIncoming",
                                (pm.ReceivedAtUtc ?? pm.SentAtUtc ?? DateTime.UtcNow).Year
                            ),
                            account.TenantId,
                            ct
                        );
                        if (upload.IsSuccess)
                            attachment.LinkCloudStorage(upload.Value);
                    }

                    await accounts.AddAttachmentAsync(attachment, ct);
                }

                messagesSynced++;
            }

            folder.UpdateSyncState(syncResult.Value.NewCursor, syncResult.Value.TotalMessages);
            foldersSynced++;
        }

        account.MarkSyncCompleted(full);
        log.Complete(foldersSynced, messagesSynced);
        await bus.PublishAsync(
            new EmailSyncCompletedIntegrationEvent
            {
                AccountId = account.Id,
                TenantId = account.TenantId,
                CorrelationId = correlation.CorrelationId,
                FoldersSynced = foldersSynced,
                MessagesSynced = messagesSynced,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> FailAsync(
        EmailAccountConnection account,
        EmailSyncLog log,
        string error,
        int folders,
        int messages,
        CancellationToken ct
    )
    {
        account.MarkSyncFailed(error);
        log.Fail(error, folders, messages);
        await bus.PublishAsync(
            new EmailSyncFailedIntegrationEvent
            {
                AccountId = account.Id,
                TenantId = account.TenantId,
                CorrelationId = correlation.CorrelationId,
                Error = error,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
