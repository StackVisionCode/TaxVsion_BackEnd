using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Security;

public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    public int ExpirationDays { get; set; } = 30;
}

/// <summary>
/// Crea sesiones (UserSession + RefreshToken hasheado) y emite el access token.
/// No guarda cambios: el handler persiste vía IUnitOfWork en la misma transacción.
/// </summary>
public sealed class AuthSessionIssuer(
    ISessionRepository sessions,
    ISecureTokenService tokens,
    IJwtTokenGenerator jwt,
    IRequestContext request,
    IOptions<RefreshTokenOptions> options
) : IAuthSessionIssuer
{
    private readonly RefreshTokenOptions _options = options.Value;

    public async Task<IssuedTokens> StartSessionAsync(
        User user,
        string effectiveTimeZoneId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> authMethods,
        string? deviceName,
        CancellationToken ct = default
    )
    {
        var session = UserSession.Start(user.TenantId, user.Id, deviceName, request.UserAgent, request.IpAddress);
        await sessions.AddSessionAsync(session, ct);

        var rawRefreshToken = tokens.GenerateToken(64);
        var refreshToken = RefreshToken.Create(
            user.TenantId,
            user.Id,
            session.Id,
            tokens.Hash(rawRefreshToken),
            DateTime.UtcNow.AddDays(_options.ExpirationDays)
        );
        await sessions.AddTokenAsync(refreshToken, ct);

        var accessToken = jwt.Generate(user, effectiveTimeZoneId, session.Id, roles, permissions, authMethods);

        return new IssuedTokens(accessToken.Token, rawRefreshToken, accessToken.ExpiresInSeconds, session.Id);
    }

    public async Task<IssuedTokens> RotateAsync(
        RefreshToken currentToken,
        UserSession session,
        User user,
        string effectiveTimeZoneId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> authMethods,
        CancellationToken ct = default
    )
    {
        var rawRefreshToken = tokens.GenerateToken(64);
        var replacement = RefreshToken.Create(
            user.TenantId,
            user.Id,
            session.Id,
            tokens.Hash(rawRefreshToken),
            DateTime.UtcNow.AddDays(_options.ExpirationDays)
        );

        currentToken.Rotate(replacement.Id);
        await sessions.AddTokenAsync(replacement, ct);

        var accessToken = jwt.Generate(user, effectiveTimeZoneId, session.Id, roles, permissions, authMethods);

        return new IssuedTokens(accessToken.Token, rawRefreshToken, accessToken.ExpiresInSeconds, session.Id);
    }
}
