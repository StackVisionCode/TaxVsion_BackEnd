using System.Security.Cryptography;
using System.Text;

namespace TaxVision.CloudStorage.Domain.Sharing;

/// <summary>
/// Token opaco de 32 bytes (256 bits de entropia, CSPRNG). El valor crudo se
/// muestra UNA sola vez al crear el link (ver CreateShareLinkHandler); en BD solo
/// se persiste el hash SHA-256 (ShareLink.TokenHash). El token no codifica
/// TenantId/ResourceId/ObjectKey — resolverlo siempre pasa por un lookup por hash.
/// </summary>
public sealed record ShareToken(string Value, byte[] Hash, string Last4)
{
    private const int TokenBytes = 32;

    public static ShareToken Create()
    {
        Span<byte> bytes = stackalloc byte[TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        var value = EncodeBase64Url(bytes);
        var hash = HashOf(value);
        return new ShareToken(value, hash, value[^4..]);
    }

    public static byte[] HashOf(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string EncodeBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
