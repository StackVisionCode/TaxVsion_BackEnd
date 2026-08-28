using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Mintea un JWT HS256 firmado con el secreto de test de <see cref="TenantApiFactory"/> (nunca un
/// secreto real de Auth ni de user-secrets). Claims mínimos para pasar
/// <see cref="BuildingBlocks.ActorTypeAuthorization.ClaimsPrincipalExtensions.TryGetTenantId"/>/
/// <c>TryGetUserId</c>, <c>[AllowActorTypes]</c> y <c>[HasPermission]</c> — actor_type=PlatformAdmin
/// + rol PlatformAdmin bypasean el chequeo de permiso incluso en modo
/// <c>Authorization:PermissionsSource=Projection</c>, así el test no depende de que exista una fila
/// de <c>UserPermissionsProjection</c> para el usuario sintético. Mismo patrón que
/// <c>TaxVision.Customer.Tests.Integration.JwtTestTokenFactory</c> (Fase 3).
/// </summary>
public static class JwtTestTokenFactory
{
    public static string Mint(TenantApiFactory factory, Guid tenantId, Guid userId) =>
        MintActor(factory, tenantId, userId, "PlatformAdmin");

    /// <summary>
    /// Mintea un JWT con un actor_type/rol arbitrario — para probar el gate <c>[AllowActorTypes]</c>
    /// (ej. un TenantAdmin que NO debe pasar a los endpoints de plataforma). Un actor distinto de
    /// PlatformAdmin no bypasea el chequeo de permiso, pero para los endpoints de plataforma el
    /// <c>[AllowActorTypes(PlatformAdmin)]</c> lo rechaza antes con 403.
    /// </summary>
    public static string MintActor(TenantApiFactory factory, Guid tenantId, Guid userId, string actorType)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("actor_type", actorType),
            new Claim(ClaimTypes.Role, actorType),
        };

        var token = new JwtSecurityToken(
            issuer: factory.JwtIssuer,
            audience: factory.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
