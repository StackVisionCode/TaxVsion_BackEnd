using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Domain.Accounts;

/// <summary>
/// Par de tokens OAuth cifrados en reposo. AccessToken y RefreshToken se guardan cada uno como su
/// propio EncryptedSecret (ciphertext+nonce+tag+keyVersion independientes) porque el refresh
/// proactivo (Fase 4) solo re-cifra el AccessToken — el RefreshToken puede quedar cifrado con una
/// key version más vieja hasta su propia rotación.
/// </summary>
public sealed class OAuthToken : BaseEntity
{
    private OAuthToken() { }

    public Guid ConnectionId { get; private set; }
    public EncryptedSecret AccessTokenCipher { get; private set; } = default!;
    public EncryptedSecret RefreshTokenCipher { get; private set; } = default!;
    public DateTime AccessTokenExpiresAtUtc { get; private set; }
    public DateTime RefreshedAtUtc { get; private set; }

    public static Result<OAuthToken> Create(
        Guid connectionId,
        EncryptedSecret accessTokenCipher,
        EncryptedSecret refreshTokenCipher,
        DateTime accessTokenExpiresAtUtc,
        DateTime refreshedAtUtc
    )
    {
        if (connectionId == Guid.Empty)
            return Result.Failure<OAuthToken>(new Error("OAuthToken.ConnectionId", "ConnectionId is required."));

        if (accessTokenCipher is null)
            return Result.Failure<OAuthToken>(
                new Error("OAuthToken.AccessTokenCipher", "AccessTokenCipher is required.")
            );

        if (refreshTokenCipher is null)
            return Result.Failure<OAuthToken>(
                new Error("OAuthToken.RefreshTokenCipher", "RefreshTokenCipher is required.")
            );

        return Result.Success(
            new OAuthToken
            {
                Id = Guid.NewGuid(),
                ConnectionId = connectionId,
                AccessTokenCipher = accessTokenCipher,
                RefreshTokenCipher = refreshTokenCipher,
                AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc,
                RefreshedAtUtc = refreshedAtUtc,
            }
        );
    }

    /// <summary>Reemplaza el AccessToken tras un refresh proactivo (Fase 4). El RefreshToken no cambia.</summary>
    public void UpdateAccessToken(
        EncryptedSecret accessTokenCipher,
        DateTime accessTokenExpiresAtUtc,
        DateTime refreshedAtUtc
    )
    {
        AccessTokenCipher = accessTokenCipher;
        AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc;
        RefreshedAtUtc = refreshedAtUtc;
    }

    /// <summary>
    /// Reemplaza el RefreshToken cuando el proveedor rota uno nuevo en la respuesta del refresh
    /// (Graph lo hace habitualmente; Gmail normalmente no, salvo revocación previa). Opcional —
    /// solo se llama si la respuesta del provider trae un refresh_token nuevo.
    /// </summary>
    public void UpdateRefreshToken(EncryptedSecret refreshTokenCipher) => RefreshTokenCipher = refreshTokenCipher;
}
