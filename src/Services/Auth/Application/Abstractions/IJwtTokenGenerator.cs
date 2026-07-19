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

    /// <summary>
    /// Emite un token de SERVICIO (client-credentials / M2M) para un tenant concreto, con
    /// <c>actor_type=Service</c> y los permisos indicados. No lleva usuario/sesión. Uso: workers de
    /// otros servicios que llaman a APIs internas (p. ej. Notification → CloudStorage) sin request de usuario.
    /// </summary>
    AccessToken GenerateServiceToken(
        Guid tenantId,
        string clientId,
        IReadOnlyCollection<string> permissions,
        int lifetimeMinutes
    );

    /// <summary>
    /// Emite un ticket de un solo propósito: prueba criptográfica de que <paramref name="email"/>
    /// reservó <paramref name="slug"/> en Auth (ver ReserveSubdomainHandler). TaxVision.Tenant.Api
    /// lo valida localmente vía su policy de autorización (claim "purpose"), sin llamar de vuelta
    /// a Auth por HTTP. No lleva usuario/sesión — es una capability, no una identidad.
    /// </summary>
    AccessToken GenerateTenantRegistrationTicket(string slug, string email, DateTime expiresAtUtc);
}

/// <summary>Expone el JSON Web Key Set público cuando se firma con RS256.</summary>
public interface IJwksProvider
{
    /// <summary>JWKS serializado, o "{"keys":[]}" cuando se firma con clave simétrica.</summary>
    string GetJwksJson();
}
