using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;

namespace TaxVision.Connectors.Application.Messages;

public static class SendMessageHandler
{
    public static async Task<Result<SendMessageResult>> Handle(
        SendMessageCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IOutboundEmailProviderClientFactory clientFactory,
        ISendRateLimiter rateLimiter,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (!await rateLimiter.TryAcquireAsync(cmd.TenantId, cmd.AccountId, ct))
            return Result.Failure<SendMessageResult>(
                new Error("SendMessageHandler.RateLimited", "Send rate limit exceeded for this account.")
            );

        var accountResult = await LoadAccountForTenantAsync(cmd, accountRepository, ct);
        if (accountResult.IsFailure)
            return Result.Failure<SendMessageResult>(accountResult.Error);
        var account = accountResult.Value;

        var clientResult = clientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure<SendMessageResult>(clientResult.Error);

        var now = DateTime.UtcNow;
        SendMessageResult sendResult;
        try
        {
            // Fase 3 (hardening) — el timeout de 30s ya lo aplica el HttpClient inyectado en el
            // client tipado (DependencyInjection.AddConnectorsInfrastructure), no hace falta un
            // CancellationTokenSource local acá; `ct` solo. Si el HttpClient corta por timeout,
            // lanza OperationCanceledException con `ct` sin cancelar — el catch de abajo lo sigue
            // distinguiendo de una cancelación real del caller.
            sendResult = await clientResult.Value.SendMessageAsync(
                account.Id,
                account.EmailAddress,
                account.DisplayName,
                cmd.Message,
                ct
            );
        }
        catch (OutboundEmailSendException ex)
        {
            await RecordAuditAsync(
                account.Id,
                ex.Message,
                ex.Reason.ToString(),
                auditLogRepository,
                unitOfWork,
                now,
                ct
            );
            return Result.Failure<SendMessageResult>(new Error($"SendMessageHandler.{ex.Reason}", ex.Message));
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
            return Result.Failure<SendMessageResult>(
                new Error("SendMessageHandler.Timeout", "Sending the message timed out after 30s.")
            );
        }

        await RecordAuditAsync(
            account.Id,
            $"Sent message via {account.ProviderCode}.",
            "Success",
            auditLogRepository,
            unitOfWork,
            now,
            ct
        );

        return Result.Success(sendResult);
    }

    private static async Task<Result<TenantEmailAccount>> LoadAccountForTenantAsync(
        SendMessageCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        CancellationToken ct
    )
    {
        var accountResult = await accountRepository.GetByIdAsync(cmd.AccountId, ct);
        if (accountResult.IsFailure)
            return accountResult;

        var account = accountResult.Value;
        return account.TenantId != cmd.TenantId
            ? Result.Failure<TenantEmailAccount>(
                new Error("SendMessageHandler.Forbidden", "Account does not belong to the caller's tenant.")
            )
            : Result.Success(account);
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
            ProviderConnectionAuditAction.MessageSend,
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
