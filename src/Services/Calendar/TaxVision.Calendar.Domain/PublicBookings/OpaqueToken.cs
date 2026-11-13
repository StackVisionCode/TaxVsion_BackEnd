using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>
/// Token opaco sin sesión (manejo de la solicitud, gestión de la reserva): 32 bytes de CSPRNG en base64url.
/// En la base solo queda el SHA-256; el valor en claro viaja una vez, en el evento que lo entrega.
/// </summary>
public static class OpaqueToken
{
    public static (string Value, byte[] Hash) Create()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var value = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (value, HashOf(value));
    }

    public static byte[] HashOf(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
