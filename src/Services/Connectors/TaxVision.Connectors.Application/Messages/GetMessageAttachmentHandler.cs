using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Audit;

namespace TaxVision.Connectors.Application.Messages;

/// <summary>
/// Resuelve metadata (filename/contentType/size) vía GetMessageAsync antes de pedir los bytes —
/// nunca se confía en filename/contentType que mande el caller (el body solo trae tenantId+accountId,
/// §36 Fase 9 item 2), siempre se resuelve del lado del proveedor.
/// </summary>
public static class GetMessageAttachmentHandler
{
    public static async Task<Result<MessageAttachmentDownload>> Handle(
        GetMessageAttachmentQuery query,
        ITenantEmailAccountRepository accountRepository,
        IEmailProviderClientFactory emailClientFactory,
        IAttachmentRateLimiter rateLimiter,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (!await rateLimiter.TryAcquireAsync(query.TenantId, ct))
            return Result.Failure<MessageAttachmentDownload>(
                new Error(
                    "GetMessageAttachmentHandler.RateLimited",
                    "Attachment fetch rate limit exceeded for this tenant."
                )
            );

        var accountResult = await accountRepository.GetByIdAsync(query.AccountId, ct);
        if (accountResult.IsFailure)
            return Result.Failure<MessageAttachmentDownload>(accountResult.Error);

        var account = accountResult.Value;
        if (account.TenantId != query.TenantId)
            return Result.Failure<MessageAttachmentDownload>(
                new Error("GetMessageAttachmentHandler.Forbidden", "Account does not belong to the caller's tenant.")
            );

        var clientResult = emailClientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure<MessageAttachmentDownload>(clientResult.Error);

        var now = DateTime.UtcNow;
        try
        {
            // Fase 3 (hardening) — el timeout de 30s ya lo aplica el HttpClient inyectado en el
            // client tipado (DependencyInjection.AddConnectorsInfrastructure), no hace falta un
            // CancellationTokenSource local acá; `ct` solo. Si el HttpClient corta por timeout,
            // lanza OperationCanceledException con `ct` sin cancelar — el catch de abajo lo sigue
            // distinguiendo de una cancelación real del caller.
            var message = await clientResult.Value.GetMessageAsync(account.Id, query.ProviderMessageId, ct);
            var metadata = message.Attachments.FirstOrDefault(a => a.ProviderAttachmentId == query.AttachmentId);
            if (metadata is null)
            {
                await RecordAuditAsync(
                    account.Id,
                    $"Attachment {query.AttachmentId} not found on message {query.ProviderMessageId}.",
                    "AttachmentNotFound",
                    auditLogRepository,
                    unitOfWork,
                    now,
                    ct
                );
                return Result.Failure<MessageAttachmentDownload>(
                    new Error("GetMessageAttachmentHandler.AttachmentNotFound", "Attachment not found on the message.")
                );
            }

            var content = await clientResult.Value.GetAttachmentAsync(
                account.Id,
                query.ProviderMessageId,
                query.AttachmentId,
                ct
            );

            await RecordAuditAsync(
                account.Id,
                $"Fetched attachment {query.AttachmentId} for message {query.ProviderMessageId}.",
                "Success",
                auditLogRepository,
                unitOfWork,
                now,
                ct
            );

            return Result.Success(
                new MessageAttachmentDownload(content, metadata.Filename, metadata.ContentType, metadata.SizeBytes)
            );
        }
        catch (EmailProviderException ex)
        {
            await RecordAuditAsync(account.Id, ex.Message, "ProviderFailed", auditLogRepository, unitOfWork, now, ct);
            return Result.Failure<MessageAttachmentDownload>(
                new Error("GetMessageAttachmentHandler.ProviderFailed", ex.Message)
            );
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await RecordAuditAsync(
                account.Id,
                "Timed out after 30s.",
                "Timeout",
                auditLogRepository,
                unitOfWork,
                now,
                ct
            );
            return Result.Failure<MessageAttachmentDownload>(
                new Error("GetMessageAttachmentHandler.Timeout", "Fetching the attachment timed out after 30s.")
            );
        }
    }

    private static async Task RecordAuditAsync(
        Guid accountId,
        string detail,
        string resultCode,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        DateTime timestamp,
        CancellationToken ct
    )
    {
        var entryResult = ProviderConnectionAuditLog.Create(
            accountId,
            ProviderConnectionAuditAction.AttachmentFetch,
            detail,
            resultCode,
            timestamp
        );
        if (entryResult.IsFailure)
            return;

        await auditLogRepository.AddAsync(entryResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
