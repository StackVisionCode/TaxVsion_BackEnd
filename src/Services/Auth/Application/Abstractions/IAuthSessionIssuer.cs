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
    /// <summary>Sesión nueva cuya primera cadena de refresh es la de <paramref name="surface"/>.</summary>
    Task<IssuedTokens> StartSessionAsync(
        User user,
        string effectiveTimeZoneId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> authMethods,
        string? deviceName,
        SessionSurface surface,
        CancellationToken ct = default
    );

    /// <summary>
    /// Suma a una sesión existente la cadena de <paramref name="surface"/> (mismo <c>sid</c>, sin takeover).
    /// Si la sesión ya tenía una cadena de esa superficie, se revoca: hay una sola por sesión.
    /// </summary>
    Task<IssuedTokens> JoinSessionAsync(
        UserSession session,
        User user,
        string effectiveTimeZoneId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> authMethods,
        SessionSurface surface,
        CancellationToken ct = default
    );

    /// <summary>Rota el refresh token dentro de su cadena (misma superficie) y emite un nuevo access token.</summary>
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
