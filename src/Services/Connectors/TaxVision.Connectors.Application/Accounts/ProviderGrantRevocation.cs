using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>
/// Revocación del grant del lado del proveedor al desconectar: best-effort de verdad. Un fallo (red, timeout,
/// secreto que ya no se descifra) se registra y no frena la desconexión local; si lanzara, Wolverine
/// reintentaría todo el mensaje y la desconexión quedaría a medias.
/// </summary>
public static class ProviderGrantRevocation
{
    public static async Task<bool> TryRevokeAsync(
        TenantEmailAccount account,
        OAuthConnection connection,
        IOAuthProviderClientFactory clientFactory,
        IEncryptedSecretProtector protector,
        ILogger logger,
        CancellationToken ct
    )
    {
        var clientResult = clientFactory.Resolve(account.ProviderCode);
        if (clientResult.IsFailure || connection.Token is null)
            return false;

        try
        {
            var refreshToken = protector.Unprotect(connection.Token.RefreshTokenCipher);
            await clientResult.Value.RevokeAsync(refreshToken, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Provider revocation failed for account {AccountId} ({FailureType}); disconnected locally anyway.",
                account.Id,
                ex.GetType().Name
            );
            return false;
        }
    }
}
