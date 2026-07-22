using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Security;

public static class JwtAuthenticationRegistration
{
    /// <summary>
    /// Validación JWT en modo dual:
    /// - Si Jwt:PublicKeyPem o Jwt:PublicKeyPath están configurados ⇒ RS256
    ///   (solo Auth posee la clave privada; el resto valida con la pública).
    /// - Si no ⇒ HS256 con Jwt:Secret (configuración actual).
    /// Rutas relativas en Jwt:PublicKeyPath se resuelven desde ContentRootPath
    /// vía IWebHostEnvironment, inyectado internamente — los llamadores no
    /// necesitan pasar nada extra.
    /// </summary>
    public static IServiceCollection AddTaxVisionJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var issuer = configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is missing.");
        var audience = configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is missing.");
        var validAudiences = configuration.GetSection("Jwt:ValidAudiences").Get<string[]>();

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
                    ValidAudiences = validAudiences is { Length: > 0 } ? validAudiences : null,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        // IPostConfigureOptions se resuelve desde DI cuando las opciones se
        // solicitan por primera vez, momento en que IWebHostEnvironment ya está
        // disponible. Así evitamos pasar contentRootPath en cada Program.cs.
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>>(sp =>
        {
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            return new PostConfigureOptions<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.TokenValidationParameters.IssuerSigningKey = ResolveSigningKey(
                        configuration,
                        env.ContentRootPath
                    );
                }
            );
        });

        services.AddAuthorization();
        return services;
    }

    private static SecurityKey ResolveSigningKey(IConfiguration configuration, string? contentRootPath)
    {
        var publicKeyPem = configuration["Jwt:PublicKeyPem"];
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            var publicKeyPath = configuration["Jwt:PublicKeyPath"];
            if (!string.IsNullOrWhiteSpace(publicKeyPath))
            {
                // Rutas relativas se resuelven desde ContentRootPath (directorio del proyecto).
                // Esto permite usar "../../../../dev-keys/jwt-public.pem" en lugar de rutas absolutas.
                if (!Path.IsPathRooted(publicKeyPath) && contentRootPath is not null)
                    publicKeyPath = Path.GetFullPath(Path.Combine(contentRootPath, publicKeyPath));

                if (File.Exists(publicKeyPath))
                    publicKeyPem = File.ReadAllText(publicKeyPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(publicKeyPem))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            // Debe coincidir con SigningKeyProvider.ComputeKeyId (Auth) -- Auth firma con un
            // "kid" derivado del hash de la clave pública, y la validación por defecto de
            // JwtBearer busca la IssuerSigningKey por ese kid. Sin este KeyId, cualquier token
            // real emitido por Auth falla con "signature key was not found" aunque la clave
            // pública sea la correcta (solo pasaban los tests con JWTs hand-crafted HS256, que
            // no llevan kid).
            return new RsaSecurityKey(rsa) { KeyId = ComputeKeyId(rsa) };
        }

        var secret =
            configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException(
                "Configure Jwt:PublicKeyPem/Jwt:PublicKeyPath (RS256) or Jwt:Secret (HS256)."
            );

        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Jwt:Secret must contain at least 32 bytes.");

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>Same computation as Auth's SigningKeyProvider.ComputeKeyId — must stay in sync.</summary>
    private static string ComputeKeyId(RSA rsa)
    {
        var publicKeyInfo = rsa.ExportSubjectPublicKeyInfo();
        var hash = SHA256.HashData(publicKeyInfo);
        return Base64UrlEncoder.Encode(hash[..16]);
    }
}
