using System.Security.Cryptography;
using System.Text;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Generación y hashing de tokens y códigos con criptografía segura:
/// tokens aleatorios en Base64 URL, códigos numéricos OTP y hash SHA-256 para su almacenamiento.
/// </summary>
public sealed class SecureTokenService : ISecureTokenService
{
    public string GenerateToken(int byteLength = 32) => ToBase64Url(RandomNumberGenerator.GetBytes(byteLength));

    /// <summary>Genera un código numérico aleatorio de la cantidad de dígitos indicada, con ceros a la izquierda.</summary>
    public string GenerateNumericCode(int digits = 6)
    {
        var max = (int)Math.Pow(10, digits);
        var value = RandomNumberGenerator.GetInt32(0, max);
        return value.ToString($"D{digits}");
    }

    /// <summary>Calcula el hash SHA-256 en hexadecimal del token en claro para almacenarlo o compararlo.</summary>
    public string Hash(string rawToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
