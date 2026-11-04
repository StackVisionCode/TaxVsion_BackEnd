using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using Wolverine;

namespace TaxVision.Connectors.Application.Accounts;

public static class DisconnectAccountHandler
{
    public static async Task<Result> Handle(
        DisconnectAccountCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IOAuthConnectionRepository connectionRepository,
        IProviderWatchSubscriptionRepository watchSubscriptionRepository,
        IOAuthProviderClientFactory clientFactory,
        IEncryptedSecretProtector protector,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<DisconnectAccountCommand> logger,
        CancellationToken ct
    )
    {
        var accountResult = await accountRepository.GetByIdAsync(cmd.AccountId, ct);
        if (accountResult.IsFailure)
            return Result.Failure(accountResult.Error);

        var account = accountResult.Value;
        if (account.TenantId != cmd.TenantId)
            return Result.Failure(
                new Error("DisconnectAccountHandler.Forbidden", "Account does not belong to the caller's tenant.")
            );

        var now = DateTime.UtcNow;
        var disconnectResult = account.Disconnect(now);
        if (disconnectResult.IsFailure)
            return disconnectResult;

        var connectionResult = await connectionRepository.GetByAccountIdAsync(account.Id, ct);
        if (connectionResult.IsSuccess)
        {
            var connection = connectionResult.Value;
            connection.Revoke(now);

            // Best-effort: revoca el grant del lado del proveedor (solo Gmail lo soporta de verdad,
            // ver IOAuthProviderClient.RevokeAsync). Nunca bloquea la desconexión local si falla.
            await ProviderGrantRevocation.TryRevokeAsync(account, connection, clientFactory, protector, logger, ct);
        }

        // El push de entrada deja de renovarse; si no, la renovación fallaba y pasaba la cuenta a Error.
        var watchResult = await watchSubscriptionRepository.GetByAccountIdAsync(account.Id, ct);
        if (watchResult.IsSuccess)
            watchResult.Value.MarkRemoved();

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new ConnectorsTenantEmailAccountDisconnectedIntegrationEvent
            {
                CorrelationId = correlation.CorrelationId,
                TenantId = cmd.TenantId,
                AccountId = account.Id,
                EmailAddress = account.EmailAddress,
                ProviderCode = account.ProviderCode.ToString(),
                DisconnectedAtUtc = now,
            }
        );

        var auditResult = ProviderConnectionAuditLog.Create(
            account.Id,
            ProviderConnectionAuditAction.Disconnect,
            $"Disconnected {account.EmailAddress}.",
            "Success",
            now
        );
        if (auditResult.IsSuccess)
        {
            await auditLogRepository.AddAsync(auditResult.Value, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
