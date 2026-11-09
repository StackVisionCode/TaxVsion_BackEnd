using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
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
        IOAuthProviderClientFactory clientFactory,
        OAuthGrantStore grants,
        AccountProviderSwitcher providerSwitcher,
        AccountConnectionFinisher finisher,
        IUserPermissionsProjectionReader initiatorProjections,
        IUnitOfWork unitOfWork,
        IOutboxUnit unit,
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
        var targetResult = await grants.ResolveTargetAsync(cmd, emailAddress, client, now, ct);
        if (targetResult.IsFailure)
            return Result.Failure<CompleteOAuthConnectResult>(targetResult.Error);
        var target = targetResult.Value;
        var account = target.Account;

        // Fila desconectada que era personal de otra persona (p. ej. un retirado re-invitado): pasa a quien conecta.
        if (!target.IsNewAccount)
            account.ReassignOwnerOnReconnect(cmd.AsOffice ? null : cmd.InitiatedByUserId);

        var stored = await grants.StoreAsync(target, cmd.ProviderCode, client, grant, now, ct);
        if (stored.IsFailure)
            return Result.Failure<CompleteOAuthConnectResult>(stored.Error);

        // Reconectar sobre un account existente con OTRO proveedor (p.ej. IMAP → Gmail OAuth): antes se dejaba el
        // ProviderCode viejo + SMTP/IMAP sin borrar, dejando a la UI mostrando el proveedor incorrecto y las
        // credenciales viejas como "zombies" en la DB. Ver ProviderSwitchPolicy.
        if (!target.IsNewAccount)
        {
            var switched = await providerSwitcher.SwitchAsync(account, cmd.ProviderCode, now, ct);
            if (switched.IsFailure)
                return Result.Failure<CompleteOAuthConnectResult>(switched.Error);
        }

        // Un grant nuevo prueba que alguien vigente tiene acceso: se cierra una revisión pendiente.
        account.ClearReview();

        await unitOfWork.SaveChangesAsync(ct);

        if (target.WidensActiveGrant && account.Status == TenantEmailAccountStatus.Active)
        {
            await finisher.AuditConnectedAsync(
                account,
                $"Updated permissions for {account.EmailAddress} via {cmd.ProviderCode}.",
                now,
                ct
            );
            return Result.Success(new CompleteOAuthConnectResult(account.Id, account.EmailAddress));
        }

        var watchResult = await finisher.ActivateAsync(cmd.TenantId, account.Id, unit, ct);
        if (watchResult.IsFailure)
            return Result.Failure<CompleteOAuthConnectResult>(watchResult.Error);

        await finisher.AuditConnectedAsync(
            account,
            $"Connected {account.EmailAddress} via {cmd.ProviderCode}.",
            now,
            ct
        );

        return Result.Success(new CompleteOAuthConnectResult(account.Id, account.EmailAddress));
    }
}
