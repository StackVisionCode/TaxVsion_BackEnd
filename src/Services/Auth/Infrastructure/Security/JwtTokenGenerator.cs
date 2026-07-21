using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

public sealed class JwtTokenGenerator(IOptions<JwtOptions> options, SigningKeyProvider signingKeys) : IJwtTokenGenerator
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken Generate(
        User user,
        string effectiveTimeZoneId,
        Guid sessionId,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> authMethods
    )
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
            new("perm_v", user.PermissionsVersion.ToString()),
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
            signingCredentials: signingKeys.GetSigningCredentials()
        );

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), _options.AccessMinutes * 60);
    }

    public AccessToken GenerateServiceToken(
        Guid tenantId,
        string clientId,
        IReadOnlyCollection<string> permissions,
        int lifetimeMinutes
    ) => GenerateScopedServiceToken(tenantId, clientId, permissions, [], _options.Audience, lifetimeMinutes);

    public AccessToken GenerateScopedServiceToken(
        Guid tenantId,
        string clientId,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<string> scopes,
        string audience,
        int lifetimeMinutes
    )
    {
        var now = DateTime.UtcNow;
        var subject = DeriveServicePrincipalId(clientId);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("tenant_id", tenantId.ToString()),
            new("actor_type", "Service"),
            new("client_id", clientId),
        };
        claims.AddRange(permissions.Select(permission => new Claim("perm", permission)));
        claims.AddRange(scopes.Select(scope => new Claim("scope", scope)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: string.IsNullOrWhiteSpace(audience) ? _options.Audience : audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(lifetimeMinutes),
            signingCredentials: signingKeys.GetSigningCredentials()
        );

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), lifetimeMinutes * 60);
    }

    public AccessToken GenerateTenantRegistrationTicket(string slug, string email, DateTime expiresAtUtc)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("purpose", "tenant-registration"),
            new("reg_slug", slug),
            new("reg_email", email),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: signingKeys.GetSigningCredentials()
        );

        var lifetimeSeconds = Math.Max(0, (int)(expiresAtUtc - now).TotalSeconds);
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), lifetimeSeconds);
    }

    /// <summary>Guid estable derivado del clientId para usarlo como 'sub' del principal de servicio.</summary>
    private static Guid DeriveServicePrincipalId(string clientId) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"service-principal:{clientId}")));
}
