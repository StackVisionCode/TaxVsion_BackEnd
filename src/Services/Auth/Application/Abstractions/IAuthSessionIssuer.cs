using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Abstractions;

public sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds, Guid SessionId);

/// <summary>
/// Orquesta la emisión de sesiones: crea UserSession + RefreshToken y genera el JWT.
/// No invoca SaveChanges; el handler persiste vía IUnitOfWork.
/// </summary>
public interface IAuthSessionIssuer
{
    Task<IssuedTokens> StartSessionAsync(
        User user,
        string effectiveTimeZoneId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> authMethods,
        string? deviceName,
        CancellationToken ct = default
    );

    /// <summary>Rota el refresh token dentro de la misma sesión y emite un nuevo access token.</summary>
    Task<IssuedTokens> RotateAsync(
        RefreshToken currentToken,
        UserSession session,
        User user,
        string effectiveTimeZoneId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> authMethods,
        CancellationToken ct = default
    );
}
