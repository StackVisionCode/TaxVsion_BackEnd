namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Denylist en Redis por sesión (claim sid) para revocación inmediata de access
/// tokens vigentes. El TTL debe cubrir la vida restante máxima del access token.
/// </summary>
public interface IAccessTokenDenylist
{
    Task DenySessionAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default);
    Task<bool> IsSessionDeniedAsync(Guid sessionId, CancellationToken ct = default);
}
