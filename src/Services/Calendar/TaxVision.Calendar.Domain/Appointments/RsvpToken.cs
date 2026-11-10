using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Calendar.Domain.Appointments;

/// <summary>
/// El enlace con que un invitado responde desde el correo, sin sesión: 32 bytes de CSPRNG en base64url.
/// En base solo queda el SHA-256; el valor en claro viaja una vez, en el evento del aviso que lo lleva.
/// </summary>
public sealed record RsvpToken(string Value, byte[] Hash)
{
    public static RsvpToken Create()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var value = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new RsvpToken(value, HashOf(value));
    }

    public static byte[] HashOf(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}

/// <summary>El token en claro de un invitado, para el aviso que sale con esta operación.</summary>
public sealed record AttendeeRsvpToken(Guid AttendeeId, string Token);
