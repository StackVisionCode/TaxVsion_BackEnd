using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Security;

/// <summary>
/// Proveedor de claves RSA para el JWT de signer. Estrategia:
/// <list type="bullet">
///   <item>Si <c>Signature:SignerJwt:PrivateKeyPem</c> apunta a un archivo válido, se
///     carga desde disco (dev/prod con Vault-mount).</item>
///   <item>Caso contrario se genera una clave ephemeral en memoria (dev only —
///     invalida los tokens al reiniciar).</item>
/// </list>
/// <para>
/// El <c>Kid</c> se deriva del SHA-256 del módulo público — determinista sin necesidad
/// de estado externo. Al agregar rotación real se guardará una lista de RSA+kid.
/// </para>
/// </summary>
public sealed class RsaSigningKeyProvider : IRsaKeyProvider, IDisposable
{
    private readonly RSA _rsa;

    public string ActiveKid { get; }

    public RsaSigningKeyProvider(IConfiguration configuration, ILogger<RsaSigningKeyProvider> logger)
    {
        _rsa = LoadOrCreateKey(configuration, logger);
        ActiveKid = ComputeKid(_rsa);
    }

    public IReadOnlyList<RsaSigningPublicKey> GetPublicKeys()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        return new List<RsaSigningPublicKey>
        {
            new(
                Kid: ActiveKid,
                N: Base64UrlEncode(parameters.Modulus!),
                E: Base64UrlEncode(parameters.Exponent!),
                Alg: "RS256"
            ),
        };
    }

    public byte[] SignSha256(byte[] material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return _rsa.SignData(material, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public void Dispose() => _rsa.Dispose();

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private static RSA LoadOrCreateKey(IConfiguration configuration, ILogger logger)
    {
        var pemPath = configuration["Signature:SignerJwt:PrivateKeyPem"];
        if (!string.IsNullOrWhiteSpace(pemPath) && File.Exists(pemPath))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(pemPath));
            logger.LogInformation("Loaded Signature signer RSA key from {Path}.", pemPath);
            return rsa;
        }
        logger.LogWarning(
            "No Signature:SignerJwt:PrivateKeyPem configured. Generating ephemeral RSA-2048 key (dev only)."
        );
        return RSA.Create(2048);
    }

    private static string ComputeKid(RSA rsa)
    {
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        var hash = SHA256.HashData(parameters.Modulus!);
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
