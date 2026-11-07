using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using Wolverine.Attributes;

namespace TaxVision.Connectors.Application.Accounts;

public static class DisconnectAccountHandler
{
    [NonTransactional]
    public static async Task<Result> Handle(
        DisconnectAccountCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IOAuthConnectionRepository connectionRepository,
        IProviderWatchSubscriptionRepository watchSubscriptionRepository,
        IOAuthProviderClientFactory clientFactory,
        IEncryptedSecretProtector protector,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IOutboxUnit unit,
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
        var disconnectResult = Result.Success();
        OAuthConnection? connection = null;
        await unit.RunAsync(
            async (outbox, token) =>
            {
                disconnectResult = account.Disconnect(now);
                if (disconnectResult.IsFailure)
                    return false;

                var connectionResult = await connectionRepository.GetByAccountIdAsync(account.Id, token);
                if (connectionResult.IsSuccess)
                {
                    connection = connectionResult.Value;
                    connection.Revoke(now);
                }

                // El push de entrada deja de renovarse; si no, la renovación fallaba y pasaba la cuenta a Error.
                var watchResult = await watchSubscriptionRepository.GetByAccountIdAsync(account.Id, token);
                if (watchResult.IsSuccess)
                    watchResult.Value.MarkRemoved();

                await outbox.PublishAsync(
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
                    await auditLogRepository.AddAsync(auditResult.Value, token);
                return true;
            },
            ct
        );
        if (disconnectResult.IsFailure)
            return disconnectResult;

        // Best-effort y ya sin transacción: revoca el grant del lado del proveedor (solo Gmail lo soporta de
        // verdad, ver IOAuthProviderClient.RevokeAsync). Si falla, la cuenta queda Disconnected igual.
        if (connection is not null)
            await ProviderGrantRevocation.TryRevokeAsync(account, connection, clientFactory, protector, logger, ct);

        return Result.Success();
    }
}
