using BuildingBlocks.Common;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Audit;
using TaxVision.Connectors.Domain.Shared;
using Wolverine.Attributes;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Orquesta el flujo de conectar cuenta (D3 §12.5) tras el callback de autorización. No hace
/// MarkConnected/Activate acá — <see cref="WatchActivationService"/> ya bundlea esa transición junto
/// con el setup del watch/subscription (compartido con el endpoint de reauth manual,
/// <c>AccountsController.Reauth</c>), así que si el watch setup falla la cuenta queda Draft con la
/// connection ya persistida y el usuario puede reintentar sin volver a autorizar con el proveedor.
///
/// El canje del código y el alta del watch corren sin transacción (CON-03): cada resultado se confirma en su propio
/// guardado, así que un fallo del watch deja la connection ya persistida para reintentar sin volver a autorizar.
///
/// Reconectar (nueva grant sobre una cuenta con una OAuthConnection Revoked/Expired) reutiliza la
/// misma fila de <c>OAuthConnection</c>/<c>OAuthToken</c> en vez de crear una nueva —
/// <c>OAuthConnections.AccountId</c> tiene un índice único, así que un segundo <c>OAuthConnection</c>
/// para la misma cuenta nunca es insertable (ver <see cref="OAuthConnection.Reconnect"/>). Si
/// <see cref="OAuthConnection.CanReconnect"/> dice que no, falla limpio: el usuario debe desconectar primero.
/// </summary>
public static class CompleteOAuthConnectHandler
{
    [NonTransactional]
    public static async Task<Result<CompleteOAuthConnectResult>> Handle(
        CompleteOAuthConnectCommand cmd,
        ITenantEmailAccountRepository accountRepository,
        IOAuthConnectionRepository connectionRepository,
        IOAuthProviderClientFactory clientFactory,
        IEncryptedSecretProtector protector,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IProviderWatchSubscriptionRepository watchSubscriptionRepository,
        IWatchProviderClientFactory watchClientFactory,
        ISmtpCredentialsRepository smtpCredentialsRepository,
        IImapCredentialsRepository imapCredentialsRepository,
        IProviderSyncCursorRepository providerSyncCursorRepository,
        IUserPermissionsProjectionReader initiatorProjections,
        IUnitOfWork unitOfWork,
        IOutboxUnit unit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        // El state dura ~10 min y no se busca por usuario: si lo retiraron o suspendieron entretanto,
        // no se canjea el código ni se toca nada (va antes de cualquier efecto).
        var initiatorCheck = await ConnectInitiatorGuard.EnsureActiveAsync(
            cmd.TenantId,
            cmd.InitiatedByUserId,
            cmd.InitiatorIsPlatformAdmin,
            initiatorProjections,
            ct
        );
        if (initiatorCheck.IsFailure)
            return Result.Failure<CompleteOAuthConnectResult>(initiatorCheck.Error);

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

        // Buzón PERSONAL: aunque OAuth pruebe la propiedad, debe ser el email del propio usuario en el
        // sistema (bloquea conectar el buzón de otro). El buzón de OFICINA (compartido) no lleva esa
        // restricción — la oficina tiene su correo propio, distinto del login del admin.
        if (!cmd.AsOffice)
        {
            var identityCheck = ConnectedEmailIdentityGuard.Ensure(emailAddress, cmd.InitiatorEmail);
            if (identityCheck.IsFailure)
                return Result.Failure<CompleteOAuthConnectResult>(identityCheck.Error);
        }

        var now = DateTime.UtcNow;
        // Scoped al tenant (uniqueness (TenantId, EmailAddress), igual que Auth): el mismo buzón en
        // otro tenant NO bloquea — se crea una cuenta propia de ESTE tenant.
        var existingAccountResult = await accountRepository.GetByTenantAndEmailAsync(cmd.TenantId, emailAddress, ct);
        var isNewAccount = existingAccountResult.IsFailure;

        TenantEmailAccount account;
        OAuthConnection? existingConnection = null;
        // Reautorizar una conexión activa solo para ampliar permisos: el watch ya corre, no se rearma.
        var widensActiveGrant = false;
        if (isNewAccount)
        {
            var createResult = TenantEmailAccount.Create(
                cmd.TenantId,
                emailAddress,
                cmd.ProviderCode,
                cmd.InitiatedByUserId,
                now,
                // Oficina = sin dueño (compartido); personal = del usuario que conecta.
                ownerUserId: cmd.AsOffice ? null : cmd.InitiatedByUserId
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
                if (!existingConnection.CanReconnect(client.ConfiguredScope))
                    return Result.Failure<CompleteOAuthConnectResult>(
                        new Error(
                            "CompleteOAuthConnectHandler.AlreadyConnected",
                            "This account already has an active OAuth connection. Disconnect it first before reconnecting."
                        )
                    );
                widensActiveGrant = existingConnection.Status == OAuthConnectionStatus.Active;
            }
        }

        // Fila desconectada que era personal de otra persona (p. ej. un retirado re-invitado): pasa a quien conecta.
        if (!isNewAccount)
            account.ReassignOwnerOnReconnect(cmd.AsOffice ? null : cmd.InitiatedByUserId);

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

        // Reconectar sobre un account existente con OTRO proveedor (p.ej. IMAP → Gmail OAuth):
        // sincroniza el proveedor del aggregate con el de la nueva connection y purga credenciales
        // obsoletas. Antes se dejaba el ProviderCode viejo + SMTP/IMAP sin borrar, dejando a la UI
        // mostrando el proveedor incorrecto y las credenciales viejas como "zombies" en la DB.
        if (!isNewAccount && account.ProviderCode != cmd.ProviderCode)
        {
            var changeResult = account.ChangeProvider(cmd.ProviderCode, now);
            if (changeResult.IsFailure)
                return Result.Failure<CompleteOAuthConnectResult>(changeResult.Error);

            var staleSmtp = await smtpCredentialsRepository.GetByAccountIdAsync(account.Id, ct);
            if (staleSmtp.IsSuccess)
                smtpCredentialsRepository.Remove(staleSmtp.Value);
            var staleImap = await imapCredentialsRepository.GetByAccountIdAsync(account.Id, ct);
            if (staleImap.IsSuccess)
                imapCredentialsRepository.Remove(staleImap.Value);
            // El cursor stale (formato IMAP UIDVALIDITY:UID) reventaba el history.list de Gmail con
            // HTTP 400 "invalid history id" y dejaba el ReconciliationJob bloqueado. Purgamos para
            // que el seeder lo re-cree con el startHistoryId correcto del nuevo watch Gmail.
            var staleCursor = await providerSyncCursorRepository.GetByAccountIdAsync(account.Id, ct);
            if (staleCursor.IsSuccess)
                providerSyncCursorRepository.Remove(staleCursor.Value);
        }

        // Un grant nuevo prueba que alguien vigente tiene acceso: se cierra una revisión pendiente.
        account.ClearReview();

        await unitOfWork.SaveChangesAsync(ct);

        if (widensActiveGrant && account.Status == TenantEmailAccountStatus.Active)
        {
            await AuditAsync(
                account,
                $"Updated permissions for {account.EmailAddress} via {cmd.ProviderCode}.",
                now,
                auditLogRepository,
                unitOfWork,
                ct
            );
            return Result.Success(new CompleteOAuthConnectResult(account.Id, account.EmailAddress));
        }

        // WatchActivationService publica ConnectorsTenantEmailAccountConnected al activar (punto único
        // compartido con el reauth), así que acá ya no se publica — se evita el doble evento.
        var watchResult = await WatchActivationService.ActivateAsync(
            cmd.TenantId,
            account.Id,
            accountRepository,
            watchSubscriptionRepository,
            watchClientFactory,
            unit,
            correlation,
            ct
        );
        if (watchResult.IsFailure)
            return Result.Failure<CompleteOAuthConnectResult>(watchResult.Error);

        await AuditAsync(
            account,
            $"Connected {account.EmailAddress} via {cmd.ProviderCode}.",
            now,
            auditLogRepository,
            unitOfWork,
            ct
        );

        return Result.Success(new CompleteOAuthConnectResult(account.Id, account.EmailAddress));
    }

    private static async Task AuditAsync(
        TenantEmailAccount account,
        string detail,
        DateTime now,
        IProviderConnectionAuditLogRepository auditLogRepository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var auditResult = ProviderConnectionAuditLog.Create(
            account.Id,
            ProviderConnectionAuditAction.Connect,
            detail,
            "Success",
            now
        );
        if (auditResult.IsFailure)
            return;

        await auditLogRepository.AddAsync(auditResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
