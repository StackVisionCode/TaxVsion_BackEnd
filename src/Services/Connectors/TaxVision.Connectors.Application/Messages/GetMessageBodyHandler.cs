using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Audit;
using Wolverine;

namespace TaxVision.Connectors.Application.Messages;

public static class GetMessageBodyHandler
{
    public static async Task<Result<MessageBodyDto>> Handle(
        GetMessageBodyQuery query,
        ITenantEmailAccountRepository accountRepository,
        IEmailProviderClientFactory emailClientFactory,
        IMessageBodyRateLimiter rateLimiter,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await rateLimiter.TryAcquireAsync(query.TenantId, query.AccountId, ct))
            return Result.Failure<MessageBodyDto>(
                new Error("GetMessageBodyHandler.RateLimited", "Body fetch rate limit exceeded for this account.")
            );

        var accountResult = await accountRepository.GetByIdAsync(query.AccountId, ct);
        if (accountResult.IsFailure)
            return Result.Failure<MessageBodyDto>(accountResult.Error);

        var account = accountResult.Value;
        if (account.TenantId != query.TenantId)
            return Result.Failure<MessageBodyDto>(
                new Error("GetMessageBodyHandler.Forbidden", "Account does not belong to the caller's tenant.")
            );

        var clientResult = emailClientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure<MessageBodyDto>(clientResult.Error);

        var now = DateTime.UtcNow;
        MessageBody body;
        try
        {
            // Fase 3 (hardening) — el timeout de 30s ya lo aplica el HttpClient inyectado en el
            // client tipado (DependencyInjection.AddConnectorsInfrastructure), no hace falta un
            // CancellationTokenSource local acá; `ct` solo. Si el HttpClient corta por timeout,
            // lanza OperationCanceledException con `ct` sin cancelar — el catch de abajo lo sigue
            // distinguiendo de una cancelación real del caller.
            body = await clientResult.Value.GetMessageBodyAsync(account.Id, query.ProviderMessageId, ct);
        }
        catch (EmailProviderException ex)
        {
            await RecordAuditAsync(account.Id, ex.Message, "ProviderFailed", auditLogRepository, unitOfWork, now, ct);
            return Result.Failure<MessageBodyDto>(new Error("GetMessageBodyHandler.ProviderFailed", ex.Message));
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
            return Result.Failure<MessageBodyDto>(
                new Error("GetMessageBodyHandler.Timeout", "Fetching the message body timed out after 30s.")
            );
        }

        await RecordAuditAsync(
            account.Id,
            $"Fetched body for message {query.ProviderMessageId}.",
            "Success",
            auditLogRepository,
            unitOfWork,
            now,
            ct
        );

        await bus.PublishAsync(
            new ConnectorsMessageBodyFetchedIntegrationEvent
            {
                TenantId = account.TenantId,
                AccountId = account.Id,
                ProviderMessageId = query.ProviderMessageId,
                MimeSizeBytes = body.MimeSizeBytes,
                FetchedAtUtc = now,
            }
        );

        return Result.Success(
            new MessageBodyDto(
                body.MimeSizeBytes,
                body.HtmlBody,
                body.TextBody,
                body.Headers,
                body.Attachments.Select(a => new MessageBodyAttachmentDto(
                        a.ProviderAttachmentId,
                        a.Filename,
                        a.ContentType,
                        a.SizeBytes
                    ))
                    .ToList()
            )
        );
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
            ProviderConnectionAuditAction.BodyFetch,
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
