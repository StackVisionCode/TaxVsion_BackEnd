using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string? Secret { get; set; }
    public string Issuer { get; set; } = default!;
    public string Audience { get; set; } = default!;
    public int AccessMinutes { get; set; } = 15;

    /// <summary>Clave privada RSA en PEM (activa RS256). Preferir PrivateKeyPath con un secret montado.</summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>Ruta a un archivo PEM con la clave privada RSA (activa RS256).</summary>
    public string? PrivateKeyPath { get; set; }
}

public sealed class JwtTokenGenerator(
    IOptions<JwtOptions> options,
    SigningKeyProvider signingKeys) : IJwtTokenGenerator
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken Generate(
        User user,
        string effectiveTimeZoneId,
        Guid sessionId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> authMethods)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("sid", sessionId.ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new("actor_type", user.ActorType.ToString()),
            new("zoneinfo", effectiveTimeZoneId),
            new("perm_v", user.PermissionsVersion.ToString())
        };

        if (user.CustomerId is Guid customerId)
            claims.Add(new Claim("customer_id", customerId.ToString()));

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim("perm", permission)));
        claims.AddRange(authMethods.Select(method => new Claim(JwtRegisteredClaimNames.Amr, method)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_options.AccessMinutes),
            signingCredentials: signingKeys.GetSigningCredentials());

        return new AccessToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            _options.AccessMinutes * 60);
    }
}
