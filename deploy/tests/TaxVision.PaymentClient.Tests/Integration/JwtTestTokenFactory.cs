using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>
/// Mintea un JWT HS256 firmado con el secreto de test de <see cref="PaymentClientApiFactory"/>
/// (nunca un secreto real de Auth ni de user-secrets). Claims mínimos para pasar
/// <c>User.TryGetTenantId</c>/<c>TryGetUserId</c> — actor_type PlatformAdmin bypassea
/// [HasPermission] (ver doc-comment de Program.cs: "los admins pasan siempre") y además
/// <c>/payments-client/admin</c> está exento de <see cref="TaxVision.PaymentClient.Api.Common.TenantStatusGateMiddleware"/>,
/// así que un tenantId sintético que no existe en la BD local no dispara 403 Tenant.Unknown.
/// Mismo patrón que <c>TaxVision.PaymentApp.Tests.Integration.JwtTestTokenFactory</c> (Fase 4.13).
/// </summary>
public static class JwtTestTokenFactory
{
    public static string Mint(PaymentClientApiFactory factory, Guid tenantId, Guid userId)
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
