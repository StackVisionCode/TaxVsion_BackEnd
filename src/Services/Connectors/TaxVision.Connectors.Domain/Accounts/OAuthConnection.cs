using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Domain.Accounts;

public enum OAuthConnectionStatus
{
    Pending,
    Active,
    Expired,
    Revoked,
}

/// <summary>
/// Grant OAuth de una TenantEmailAccount (Gmail/Graph). 1:1 conceptual con la cuenta — vive como
/// entidad separada (no owned) porque tiene su propio ciclo de vida de tokens (OAuthToken) y porque
/// una futura reconexión (Fase 6 reauth) crea un OAuthConnection nuevo sin tocar TenantEmailAccount.
/// </summary>
public sealed class OAuthConnection : BaseEntity
{
    private OAuthConnection() { }

    public Guid AccountId { get; private set; }
    public ProviderCode ProviderCode { get; private set; }
    public string ClientId { get; private set; } = default!;
    public string Scope { get; private set; } = default!;
    public DateTime AuthorizedAtUtc { get; private set; }
    public OAuthConnectionStatus Status { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    private OAuthToken? _token;
    public OAuthToken? Token => _token;

    public static Result<OAuthConnection> Create(
        Guid accountId,
        ProviderCode providerCode,
        string clientId,
        string scope,
        DateTime authorizedAtUtc
    )
    {
        if (accountId == Guid.Empty)
            return Result.Failure<OAuthConnection>(new Error("OAuthConnection.AccountId", "AccountId is required."));

        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 200)
            return Result.Failure<OAuthConnection>(
                new Error("OAuthConnection.ClientId", "ClientId is required and must be at most 200 chars.")
            );

        if (string.IsNullOrWhiteSpace(scope))
            return Result.Failure<OAuthConnection>(new Error("OAuthConnection.Scope", "Scope is required."));

        return Result.Success(
            new OAuthConnection
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                ProviderCode = providerCode,
                ClientId = clientId,
                Scope = scope,
                AuthorizedAtUtc = authorizedAtUtc,
                Status = OAuthConnectionStatus.Pending,
            }
        );
    }

    /// <summary>Pending → Active. El intercambio de authorization code por tokens (Fase 4/5) tuvo éxito.</summary>
    public Result AttachToken(OAuthToken token)
    {
        if (_token is not null)
            return Result.Failure(
                new Error("OAuthConnection.TokenAlreadyAttached", "A token is already attached to this connection.")
            );

        if (Status != OAuthConnectionStatus.Pending)
            return Result.Failure(InvalidTransition(OAuthConnectionStatus.Active));

        _token = token;
        Status = OAuthConnectionStatus.Active;
        return Result.Success();
    }

    /// <summary>Active → Expired. El refresh (Fase 4) agotó los reintentos.</summary>
    public Result MarkExpired()
    {
        if (Status != OAuthConnectionStatus.Active)
            return Result.Failure(InvalidTransition(OAuthConnectionStatus.Expired));

        Status = OAuthConnectionStatus.Expired;
        return Result.Success();
    }

    /// <summary>
    /// Revoked|Expired → Active. El usuario volvió a pasar por el flujo de conectar cuenta (D3
    /// §12.5) tras haberse desconectado o expirado. Reutiliza esta misma fila en vez de crear una
    /// nueva — <c>OAuthConnections.AccountId</c> tiene un índice único, así que un segundo
    /// <c>OAuthConnection</c> para la misma cuenta nunca es insertable. El caller es responsable de
    /// actualizar el <see cref="Token"/> asociado (<c>UpdateAccessToken</c>/<c>UpdateRefreshToken</c>)
    /// con el grant nuevo — este método no lo toca.
    /// </summary>
    public Result Reconnect(string clientId, string scope, DateTime authorizedAtUtc)
    {
        if (Status is not (OAuthConnectionStatus.Revoked or OAuthConnectionStatus.Expired))
            return Result.Failure(InvalidTransition(OAuthConnectionStatus.Active));

        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 200)
            return Result.Failure(
                new Error("OAuthConnection.ClientId", "ClientId is required and must be at most 200 chars.")
            );

        if (string.IsNullOrWhiteSpace(scope))
            return Result.Failure(new Error("OAuthConnection.Scope", "Scope is required."));

        ClientId = clientId;
        Scope = scope;
        AuthorizedAtUtc = authorizedAtUtc;
        RevokedAtUtc = null;
        Status = OAuthConnectionStatus.Active;
        return Result.Success();
    }

    /// <summary>Cualquier estado salvo Revoked → Revoked. Desconexión explícita del usuario.</summary>
    public Result Revoke(DateTime revokedAtUtc)
    {
        if (Status == OAuthConnectionStatus.Revoked)
            return Result.Failure(InvalidTransition(OAuthConnectionStatus.Revoked));

        Status = OAuthConnectionStatus.Revoked;
        RevokedAtUtc = revokedAtUtc;
        return Result.Success();
    }

    private Error InvalidTransition(OAuthConnectionStatus target) =>
        new("OAuthConnection.InvalidTransition", $"Cannot transition OAuthConnection from {Status} to {target}.");
}
