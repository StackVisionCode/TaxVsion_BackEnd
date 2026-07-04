using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Proveedor de la clave de firma JWT en modo dual:
/// - Si Jwt:PrivateKeyPem o Jwt:PrivateKeyPath están configurados ⇒ RS256 (clave
///   privada solo en Auth; los demás servicios validan con la pública vía JWKS).
/// - Si no ⇒ HS256 con Jwt:Secret (compatibilidad con la configuración actual).
/// Registrado como singleton.
/// </summary>
public sealed class SigningKeyProvider : IJwksProvider, IDisposable
{
    private readonly RSA? _rsa;
    private readonly SigningCredentials _credentials;

    public bool UsesRsa => _rsa is not null;
    public string? KeyId { get; }

    public SigningKeyProvider(IOptions<JwtOptions> options)
    {
        var jwt = options.Value;

        var privateKeyPem = jwt.PrivateKeyPem;
        if (
            string.IsNullOrWhiteSpace(privateKeyPem)
            && !string.IsNullOrWhiteSpace(jwt.PrivateKeyPath)
            && File.Exists(jwt.PrivateKeyPath)
        )
        {
            privateKeyPem = File.ReadAllText(jwt.PrivateKeyPath);
        }

        if (!string.IsNullOrWhiteSpace(privateKeyPem))
        {
            _rsa = RSA.Create();
            _rsa.ImportFromPem(privateKeyPem);

            KeyId = ComputeKeyId(_rsa);
            var key = new RsaSecurityKey(_rsa) { KeyId = KeyId };
            _credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        }
        else
        {
            var secret =
                jwt.Secret
                ?? throw new InvalidOperationException(
                    "Configure Jwt:PrivateKeyPem/Jwt:PrivateKeyPath (RS256) or Jwt:Secret (HS256)."
                );
            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret));
            _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        }
    }

    public SigningCredentials GetSigningCredentials() => _credentials;

    /// <summary>JWKS público. Con HS256 devuelve un set vacío (no hay clave publicable).</summary>
    public string GetJwksJson()
    {
        if (_rsa is null)
            return """{"keys":[]}""";

        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        var n = Base64UrlEncoder.Encode(parameters.Modulus);
        var e = Base64UrlEncoder.Encode(parameters.Exponent);
        return $$"""
            {"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{KeyId}}","n":"{{n}}","e":"{{e}}"}]}
            """.Trim();
    }

    private static string ComputeKeyId(RSA rsa)
    {
        var publicKeyInfo = rsa.ExportSubjectPublicKeyInfo();
        var hash = SHA256.HashData(publicKeyInfo);
        return Base64UrlEncoder.Encode(hash[..16]);
    }

    public void Dispose() => _rsa?.Dispose();
}
