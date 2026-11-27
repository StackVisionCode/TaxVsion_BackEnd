using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
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
    IOptions<RefreshTokenOptions> options,
    IMfaRepository mfa,
    IOptions<MfaOptions> mfaOptions,
    ISessionLocationResolver locations
) : IAuthSessionIssuer
{
    private readonly RefreshTokenOptions _options = options.Value;

    public async Task<IssuedTokens> StartSessionAsync(
        User user,
        string effectiveTimeZoneId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> authMethods,
        string? deviceName,
        SessionSurface surface,
        CancellationToken ct = default
    )
    {
        var session = UserSession.Start(
            user.TenantId,
            user.Id,
            deviceName,
            request.UserAgent,
            request.IpAddress,
            surface,
            locations.Resolve(request.IpAddress)
        );
        await sessions.AddSessionAsync(session, ct);

        return await IssueChainAsync(session, user, effectiveTimeZoneId, roles, authMethods, surface, ct);
    }

    public async Task<IssuedTokens> JoinSessionAsync(
        UserSession session,
        User user,
        string effectiveTimeZoneId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> authMethods,
        SessionSurface surface,
        CancellationToken ct = default
    )
    {
        await sessions.RevokeSurfaceTokensAsync(session.Id, surface, "surface_rejoined", ct);
        return await IssueChainAsync(session, user, effectiveTimeZoneId, roles, authMethods, surface, ct);
    }

    public async Task<IssuedTokens> RotateAsync(
        RefreshToken currentToken,
        UserSession session,
        User user,
        string effectiveTimeZoneId,
        IReadOnlyCollection<string> roles,
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
            DateTime.UtcNow.AddDays(_options.ExpirationDays),
            currentToken.Surface
        );

        currentToken.Rotate(replacement.Id);
        await sessions.AddTokenAsync(replacement, ct);

        var accessToken = await GenerateAccessAsync(
            user,
            effectiveTimeZoneId,
            session.Id,
            roles,
            authMethods,
            currentToken.Surface,
            ct
        );

        return new IssuedTokens(accessToken.Token, rawRefreshToken, accessToken.ExpiresInSeconds, session.Id);
    }

    private async Task<IssuedTokens> IssueChainAsync(
        UserSession session,
        User user,
        string effectiveTimeZoneId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> authMethods,
        SessionSurface surface,
        CancellationToken ct
    )
    {
        var rawRefreshToken = tokens.GenerateToken(64);
        var refreshToken = RefreshToken.Create(
            user.TenantId,
            user.Id,
            session.Id,
            tokens.Hash(rawRefreshToken),
            DateTime.UtcNow.AddDays(_options.ExpirationDays),
            surface
        );
        await sessions.AddTokenAsync(refreshToken, ct);

        var accessToken = await GenerateAccessAsync(
            user,
            effectiveTimeZoneId,
            session.Id,
            roles,
            authMethods,
            surface,
            ct
        );

        return new IssuedTokens(accessToken.Token, rawRefreshToken, accessToken.ExpiresInSeconds, session.Id);
    }

    // Marcas restringidas en cada emisión y renovación (RestrictedTokenMarks): no depende de lo que haga el front.
    private async Task<AccessToken> GenerateAccessAsync(
        User user,
        string effectiveTimeZoneId,
        Guid sessionId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> authMethods,
        SessionSurface surface,
        CancellationToken ct
    )
    {
        var (passwordChangeOnly, enrollmentOnly) = await RestrictedTokenMarks.ResolveAsync(
            user,
            surface,
            mfa,
            mfaOptions.Value.Enforced,
            ct
        );
        return jwt.Generate(
            user,
            effectiveTimeZoneId,
            sessionId,
            roles,
            authMethods,
            surface,
            mfaEnrollmentOnly: enrollmentOnly,
            passwordChangeOnly: passwordChangeOnly
        );
    }
}
