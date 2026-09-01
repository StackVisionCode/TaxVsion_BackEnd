using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using Wolverine;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Orquesta el flujo de conectar cuenta (D3 §12.5) tras el callback de autorización. No hace
/// MarkConnected/Activate acá — <see cref="WatchActivationService"/> ya bundlea esa transición junto
/// con el setup del watch/subscription (compartido con el endpoint de reauth manual,
/// <c>AccountsController.Reauth</c>), así que si el watch setup falla la cuenta queda Draft con la
/// connection ya persistida y el usuario puede reintentar sin volver a autorizar con el proveedor.
///
/// Se llama en proceso (no vía <c>bus.InvokeAsync(new SetupWatchCommand(...))</c>) para compartir la
/// MISMA transacción/DbContext que el <c>SaveChangesAsync</c> de arriba — ver el docblock de
/// <see cref="WatchActivationService"/> para el bug real que este approach evita.
///
/// Reconectar (nueva grant sobre una cuenta con una OAuthConnection Revoked/Expired) reutiliza la
/// misma fila de <c>OAuthConnection</c>/<c>OAuthToken</c> en vez de crear una nueva —
/// <c>OAuthConnections.AccountId</c> tiene un índice único, así que un segundo <c>OAuthConnection</c>
/// para la misma cuenta nunca es insertable (ver <see cref="OAuthConnection.Reconnect"/>). Si la
/// connection existente sigue Active/Pending, falla limpio: el usuario debe desconectar primero.
/// </summary>
public static class CompleteOAuthConnectHandler
{
    public static async Task<Result<CompleteOAuthConnectResult>> Handle(
        CompleteOAuthConnectCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IOAuthConnectionRepository connectionRepository,
        IOAuthProviderClientFactory clientFactory,
        IEncryptedSecretProtector protector,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IProviderWatchSubscriptionRepository watchSubscriptionRepository,
        IWatchProviderClientFactory watchClientFactory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var clientResult = clientFactory.Resolve(cmd.ProviderCode);
        if (clientResult.IsFailure)
            return Result.Failure<CompleteOAuthConnectResult>(clientResult.Error);
        var client = clientResult.Value;

        OAuthTokenGrant grant;
        string emailAddress;
        try
        {
            grant = await client.ExchangeAuthorizationCodeAsync(cmd.AuthorizationCode, client.RedirectUri, ct);
            emailAddress = await client.GetAuthorizedEmailAddressAsync(grant.AccessToken, ct);
        }
        catch (OAuthProviderException ex)
        {
            return Result.Failure<CompleteOAuthConnectResult>(
                new Error("CompleteOAuthConnectHandler.ProviderFailed", ex.Message)
            );
        }

        if (string.IsNullOrWhiteSpace(grant.RefreshToken))
            return Result.Failure<CompleteOAuthConnectResult>(
                new Error(
                    "CompleteOAuthConnectHandler.MissingRefreshToken",
                    "The provider did not return a refresh token on first authorization — the user must be re-prompted for consent (offline access)."
                )
            );

        // Política estricta: aunque OAuth pruebe la propiedad del buzón, debe ser el email del propio
        // usuario en el sistema (bloquea conectar el buzón de otro). El email vino en el state.
        var identityCheck = ConnectedEmailIdentityGuard.Ensure(emailAddress, cmd.InitiatorEmail);
        if (identityCheck.IsFailure)
            return Result.Failure<CompleteOAuthConnectResult>(identityCheck.Error);

        var now = DateTime.UtcNow;
        // Scoped al tenant (uniqueness (TenantId, EmailAddress), igual que Auth): el mismo buzón en
        // otro tenant NO bloquea — se crea una cuenta propia de ESTE tenant.
        var existingAccountResult = await accountRepository.GetByTenantAndEmailAsync(cmd.TenantId, emailAddress, ct);
        var isNewAccount = existingAccountResult.IsFailure;

        TenantEmailAccount account;
        OAuthConnection? existingConnection = null;
        if (isNewAccount)
        {
            var createResult = TenantEmailAccount.Create(
                cmd.TenantId,
                emailAddress,
                cmd.ProviderCode,
                cmd.InitiatedByUserId,
                now
            );
            if (createResult.IsFailure)
                return Result.Failure<CompleteOAuthConnectResult>(createResult.Error);

            account = createResult.Value;
        }
        else
        {
            // El lookup es tenant-scoped, así que la cuenta ya es de ESTE tenant (no hace falta
            // re-chequear TenantId — antes había un guard global que bloqueaba el mismo buzón en
            // otro tenant; se quitó a propósito para alinear con el modelo (TenantId, Email)).
            account = existingAccountResult.Value;

            var existingConnectionResult = await connectionRepository.GetByAccountIdAsync(account.Id, ct);
            if (existingConnectionResult.IsSuccess)
            {
                existingConnection = existingConnectionResult.Value;
                if (existingConnection.Status is OAuthConnectionStatus.Active or OAuthConnectionStatus.Pending)
                    return Result.Failure<CompleteOAuthConnectResult>(
                        new Error(
                            "CompleteOAuthConnectHandler.AlreadyConnected",
                            "This account already has an active OAuth connection. Disconnect it first before reconnecting."
                        )
                    );
            }
        }

        if (existingConnection is not null)
        {
            var reconnectResult = existingConnection.Reconnect(client.ClientId, client.ConfiguredScope, now);
            if (reconnectResult.IsFailure)
                return Result.Failure<CompleteOAuthConnectResult>(reconnectResult.Error);

            existingConnection.Token!.UpdateAccessToken(
                protector.Protect(grant.AccessToken),
                now.AddSeconds(grant.ExpiresInSeconds),
                now
            );
            existingConnection.Token.UpdateRefreshToken(protector.Protect(grant.RefreshToken));
        }
        else
        {
            var connectionResult = OAuthConnection.Create(
                account.Id,
                cmd.ProviderCode,
                client.ClientId,
                client.ConfiguredScope,
                now
            );
            if (connectionResult.IsFailure)
                return Result.Failure<CompleteOAuthConnectResult>(connectionResult.Error);
            var connection = connectionResult.Value;

            var tokenResult = OAuthToken.Create(
                connection.Id,
                protector.Protect(grant.AccessToken),
                protector.Protect(grant.RefreshToken),
                now.AddSeconds(grant.ExpiresInSeconds),
                now
            );
            if (tokenResult.IsFailure)
                return Result.Failure<CompleteOAuthConnectResult>(tokenResult.Error);

            var attachResult = connection.AttachToken(tokenResult.Value);
            if (attachResult.IsFailure)
                return Result.Failure<CompleteOAuthConnectResult>(attachResult.Error);

            if (isNewAccount)
                await accountRepository.AddAsync(account, ct);
            await connectionRepository.AddAsync(connection, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        var watchResult = await WatchActivationService.ActivateAsync(
            cmd.TenantId,
            account.Id,
            accountRepository,
            watchSubscriptionRepository,
            watchClientFactory,
            unitOfWork,
            ct
        );
        if (watchResult.IsFailure)
            return Result.Failure<CompleteOAuthConnectResult>(watchResult.Error);

        await bus.PublishAsync(
            new ConnectorsTenantEmailAccountConnectedIntegrationEvent
            {
                CorrelationId = correlation.CorrelationId,
                TenantId = cmd.TenantId,
                AccountId = account.Id,
                EmailAddress = account.EmailAddress,
                ProviderCode = cmd.ProviderCode.ToString(),
                ConnectedAtUtc = now,
            }
        );

        var auditResult = ProviderConnectionAuditLog.Create(
            account.Id,
            ProviderConnectionAuditAction.Connect,
            $"Connected {account.EmailAddress} via {cmd.ProviderCode}.",
            "Success",
            now
        );
        if (auditResult.IsSuccess)
        {
            await auditLogRepository.AddAsync(auditResult.Value, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success(new CompleteOAuthConnectResult(account.Id, account.EmailAddress));
    }
}
