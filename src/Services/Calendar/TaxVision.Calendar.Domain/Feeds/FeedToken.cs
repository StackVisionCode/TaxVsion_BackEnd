using System.Security.Cryptography;
using System.Text;

namespace TaxVision.Calendar.Domain.Feeds;

/// <summary>
/// La credencial del feed: 32 bytes de CSPRNG en base64url.
///
/// <para>
/// Opaco y no firmado. Un HMAC ahorraría la consulta, pero el token tiene que poder revocarse desde
/// la UI y eso obliga a una fila igual — con la fila, la firma no aporta nada y sí una segunda forma
/// de validar credenciales en el repositorio. Mismo patrón que <c>ShareToken</c> de CloudStorage.
/// </para>
///
/// <para>El valor crudo se ve una sola vez, al emitirlo. En base sólo queda el SHA-256.</para>
/// </summary>
public sealed record FeedToken(string Value, byte[] Hash, string Last4)
{
    private const int TokenBytes = 32;

    public static FeedToken Create()
    {
        Span<byte> bytes = stackalloc byte[TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        var value = Encode(bytes);
        return new FeedToken(value, HashOf(value), value[^4..]);
    }

    public static byte[] HashOf(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
