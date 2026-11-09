using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace TaxVision.ContentScanner.Scanning;

/// <summary>
/// Clave compartida con CloudStorage. Se compara por hash: los dos lados miden 32 bytes, así que el tiempo de
/// respuesta no revela ni cuánto coincide ni la longitud de la clave.
/// </summary>
public sealed class ScannerApiKey(IOptions<ContentScannerOptions> options)
{
    private readonly byte[] _expectedHash = Hash(options.Value.ApiKey);

    public bool Matches(string provided) => CryptographicOperations.FixedTimeEquals(Hash(provided), _expectedHash);

    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));
}
