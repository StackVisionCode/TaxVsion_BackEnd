using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Abstractions;

public sealed record AccessToken(string Token, int ExpiresInSeconds);

public interface IJwtTokenGenerator
{
    /// <summary>
    /// Emite un access token JWT con claims: sub, email, tenant_id, actor_type,
    /// customer_id, zoneinfo, sid, jti, amr, roles, perm y perm_v.
    /// </summary>
    AccessToken Generate(
        User user,
        string effectiveTimeZoneId,
        Guid sessionId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> authMethods
    );
}

/// <summary>Expone el JSON Web Key Set público cuando se firma con RS256.</summary>
public interface IJwksProvider
{
    /// <summary>JWKS serializado, o "{"keys":[]}" cuando se firma con clave simétrica.</summary>
    string GetJwksJson();
}
