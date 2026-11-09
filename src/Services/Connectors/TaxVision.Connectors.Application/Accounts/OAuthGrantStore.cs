using BuildingBlocks.Results;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>A qué cuenta va el grant y si ya tiene conexión.</summary>
/// <param name="WidensActiveGrant">Reautorizar una conexión activa solo para ampliar permisos: el watch ya corre, no se rearma.</param>
public sealed record OAuthConnectTarget(
    TenantEmailAccount Account,
    bool IsNewAccount,
    OAuthConnection? ExistingConnection,
    bool WidensActiveGrant
);

/// <summary>Busca o crea la cuenta del grant y deja la conexión con sus tokens cifrados. El guardado es de quien llama.</summary>
public sealed class OAuthGrantStore(
    ITenantEmailAccountRepository accounts,
    IOAuthConnectionRepository connections,
    IEncryptedSecretProtector protector
)
{
    public async Task<Result<OAuthConnectTarget>> ResolveTargetAsync(
        CompleteOAuthConnectCommand cmd,
        string emailAddress,
        IOAuthProviderClient client,
        DateTime now,
        CancellationToken ct
    )
    {
        // Scoped al tenant (uniqueness (TenantId, EmailAddress), igual que Auth): el mismo buzón en
        // otro tenant NO bloquea — se crea una cuenta propia de ESTE tenant.
        var existingAccountResult = await accounts.GetByTenantAndEmailAsync(cmd.TenantId, emailAddress, ct);
        if (existingAccountResult.IsFailure)
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
                return Result.Failure<OAuthConnectTarget>(createResult.Error);

            return Result.Success(new OAuthConnectTarget(createResult.Value, true, null, false));
        }

        // El lookup es tenant-scoped, así que la cuenta ya es de ESTE tenant (no hace falta
        // re-chequear TenantId — antes había un guard global que bloqueaba el mismo buzón en
        // otro tenant; se quitó a propósito para alinear con el modelo (TenantId, Email)).
        var account = existingAccountResult.Value;

        var existingConnectionResult = await connections.GetByAccountIdAsync(account.Id, ct);
        if (existingConnectionResult.IsFailure)
            return Result.Success(new OAuthConnectTarget(account, false, null, false));

        var connection = existingConnectionResult.Value;
        if (!connection.CanReconnect(client.ConfiguredScope))
            return Result.Failure<OAuthConnectTarget>(
                new Error(
                    "CompleteOAuthConnectHandler.AlreadyConnected",
                    "This account already has an active OAuth connection. Disconnect it first before reconnecting."
                )
            );

        return Result.Success(
            new OAuthConnectTarget(account, false, connection, connection.Status == OAuthConnectionStatus.Active)
        );
    }

    public async Task<Result> StoreAsync(
        OAuthConnectTarget target,
        ProviderCode providerCode,
        IOAuthProviderClient client,
        OAuthTokenGrant grant,
        DateTime now,
        CancellationToken ct
    )
    {
        if (target.ExistingConnection is { } existing)
        {
            var reconnectResult = existing.Reconnect(client.ClientId, client.ConfiguredScope, now);
            if (reconnectResult.IsFailure)
                return reconnectResult;

            existing.Token!.UpdateAccessToken(
                protector.Protect(grant.AccessToken),
                now.AddSeconds(grant.ExpiresInSeconds),
                now
            );
            existing.Token.UpdateRefreshToken(protector.Protect(grant.RefreshToken!));
            return Result.Success();
        }

        var connectionResult = OAuthConnection.Create(
            target.Account.Id,
            providerCode,
            client.ClientId,
            client.ConfiguredScope,
            now
        );
        if (connectionResult.IsFailure)
            return Result.Failure(connectionResult.Error);
        var connection = connectionResult.Value;

        var tokenResult = OAuthToken.Create(
            connection.Id,
            protector.Protect(grant.AccessToken),
            protector.Protect(grant.RefreshToken!),
            now.AddSeconds(grant.ExpiresInSeconds),
            now
        );
        if (tokenResult.IsFailure)
            return Result.Failure(tokenResult.Error);

        var attachResult = connection.AttachToken(tokenResult.Value);
        if (attachResult.IsFailure)
            return attachResult;

        if (target.IsNewAccount)
            await accounts.AddAsync(target.Account, ct);
        await connections.AddAsync(connection, ct);
        return Result.Success();
    }
}
