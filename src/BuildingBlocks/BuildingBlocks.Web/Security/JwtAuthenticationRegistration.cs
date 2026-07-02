using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Security;

public static class JwtAuthenticationRegistration
{
    /// <summary>
    /// Validación JWT en modo dual:
    /// - Si Jwt:PublicKeyPem o Jwt:PublicKeyPath están configurados ⇒ RS256
    ///   (solo Auth posee la clave privada; el resto valida con la pública).
    /// - Si no ⇒ HS256 con Jwt:Secret (configuración actual).
    /// </summary>
    public static IServiceCollection AddTaxVisionJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer is missing.");
        var audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience is missing.");

        var signingKey = ResolveSigningKey(configuration);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization();
        return services;
    }

    private static SecurityKey ResolveSigningKey(IConfiguration configuration)
    {
        var publicKeyPem = configuration["Jwt:PublicKeyPem"];
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            var publicKeyPath = configuration["Jwt:PublicKeyPath"];
            if (!string.IsNullOrWhiteSpace(publicKeyPath) && File.Exists(publicKeyPath))
                publicKeyPem = File.ReadAllText(publicKeyPath);
        }

        if (!string.IsNullOrWhiteSpace(publicKeyPem))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return new RsaSecurityKey(rsa);
        }

        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException(
                "Configure Jwt:PublicKeyPem/Jwt:PublicKeyPath (RS256) or Jwt:Secret (HS256).");

        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Jwt:Secret must contain at least 32 bytes.");

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }
}
