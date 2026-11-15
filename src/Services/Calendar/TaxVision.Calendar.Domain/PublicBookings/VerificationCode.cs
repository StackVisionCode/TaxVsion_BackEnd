using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Calendar.Domain.PublicBookings;

/// <summary>Código de 6 dígitos que prueba que el email es del visitante. Su hash va atado a la reserva.</summary>
public static class VerificationCode
{
    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public static byte[] HashOf(Guid bookingId, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{bookingId:N}:{code}"));

    public static bool Matches(Guid bookingId, string? code, byte[]? hash)
    {
        if (hash is null || code is null || code.Length != 6 || code.AsSpan().ContainsAnyExceptInRange('0', '9'))
            return false;
        return CryptographicOperations.FixedTimeEquals(HashOf(bookingId, code), hash);
    }
}
