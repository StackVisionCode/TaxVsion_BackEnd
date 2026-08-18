using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// Mintea un JWT HS256 firmado con el secreto de test de <see cref="CorrespondenceApiFactory"/>
/// (nunca un secreto real de Auth ni de user-secrets). Claims mínimos para pasar
/// <see cref="BuildingBlocks.ActorTypeAuthorization.ClaimsPrincipalExtensions.TryGetTenantId"/>/
/// <c>TryGetUserId</c>, <c>[AllowActorTypes]</c> y <c>[HasPermission]</c> — actor_type=PlatformAdmin
/// + rol PlatformAdmin bypasean el chequeo de permiso incluso en modo
/// <c>Authorization:PermissionsSource=Projection</c>, así el test no depende de que exista una fila
/// de <c>UserPermissionsProjection</c> para el usuario sintético. Mismo patrón que
/// <c>TaxVision.Connectors.Tests.Integration.JwtTestTokenFactory</c> (Fase 4.8).
/// </summary>
public static class JwtTestTokenFactory
{
    public static string Mint(CorrespondenceApiFactory factory, Guid tenantId, Guid userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("actor_type", "PlatformAdmin"),
            new Claim(ClaimTypes.Role, "PlatformAdmin"),
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
