using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// Mintea un JWT HS256 firmado con el secreto de test de <see cref="CloudStorageApiFactory"/>
/// (nunca un secreto real de Auth ni de user-secrets). Claims mínimos para pasar
/// <see cref="BuildingBlocks.ActorTypeAuthorization.ClaimsPrincipalExtensions.TryGetTenantId"/>/
/// <c>TryGetUserId</c>, <c>[AllowActorTypes]</c> y <c>[HasPermission]</c> — actor_type=PlatformAdmin
/// + rol PlatformAdmin bypasean el chequeo de permiso incluso en modo
/// <c>Authorization:PermissionsSource=Projection</c>, así el test no depende de que exista una fila
/// de <c>UserPermissionsProjection</c> para el usuario sintético. Mismo patrón que
/// <c>TaxVision.Scribe.Tests.Integration.JwtTestTokenFactory</c> (Fase 4.5).
/// </summary>
public static class JwtTestTokenFactory
{
    public static string Mint(CloudStorageApiFactory factory, Guid tenantId, Guid userId) =>
        Mint(factory, tenantId, userId, "PlatformAdmin");

    /// <summary>Token de otro tipo de actor; el portal lleva su cliente.</summary>
    public static string Mint(CloudStorageApiFactory factory, Guid tenantId, Guid userId, string actorType)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("actor_type", actorType),
        };
        if (actorType == "PlatformAdmin")
            claims.Add(new Claim(ClaimTypes.Role, "PlatformAdmin"));
        if (actorType == "CustomerPortal")
            claims.Add(new Claim("customer_id", Guid.NewGuid().ToString()));

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
