using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Signature.Domain.Delivery;

/// <summary>
/// Secreto del enlace de entrega: 32 bytes aleatorios en base64url. Solo se guarda su SHA-256; el valor
/// plano sale una vez, cifrado, hacia el correo.
/// </summary>
public sealed record DeliveryToken(string Plain, string Hash)
{
    public const int HashLength = 64;

    public static DeliveryToken Generate()
    {
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new DeliveryToken(plain, HashOf(plain));
    }

    public static string HashOf(string plain) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();
}
