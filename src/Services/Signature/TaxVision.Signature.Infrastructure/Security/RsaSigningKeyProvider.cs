using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Infrastructure.Security;

/// <summary>
/// Proveedor de claves RSA para el JWT de signer. Estrategia:
/// <list type="bullet">
///   <item>Si <c>Signature:SignerJwt:PrivateKeyPem</c> está configurado, se carga ese archivo. Si no existe o no
///     es una clave RSA privada de al menos 2048 bits, el servicio no arranca (es un error de configuración).</item>
///   <item>Sin configurar: en Development se genera una clave efímera (invalida los tokens al reiniciar); en
///     cualquier otro entorno el servicio no arranca — con una efímera cada redeploy mataba todos los enlaces
///     de firma ya enviados.</item>
/// </list>
/// <para>
/// El <c>Kid</c> se deriva del SHA-256 del módulo público — determinista sin necesidad
/// de estado externo. Al agregar rotación real se guardará una lista de RSA+kid.
/// </para>
/// </summary>
public sealed class RsaSigningKeyProvider : IRsaKeyProvider, IDisposable
{
    public const int MinimumKeySizeBits = 2048;

    private readonly RSA _rsa;

    public string ActiveKid { get; }

    public RsaSigningKeyProvider(
        IOptions<SignerJwtOptions> options,
        IHostEnvironment environment,
        ILogger<RsaSigningKeyProvider> logger
    )
    {
        _rsa = LoadOrCreateKey(options.Value.PrivateKeyPem, environment, logger);
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

    private static RSA LoadOrCreateKey(string? pemPath, IHostEnvironment environment, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(pemPath))
            return LoadFromFile(pemPath, logger);

        if (!environment.IsDevelopment())
            throw new InvalidOperationException(
                $"{SignerJwtOptions.SectionName}:PrivateKeyPem is not configured. Outside Development the signer "
                    + "key must be a mounted PEM file; an ephemeral key would invalidate every signing link on restart."
            );

        logger.LogWarning(
            "No Signature:SignerJwt:PrivateKeyPem configured. Generating ephemeral RSA-2048 key (dev only)."
        );
        return RSA.Create(MinimumKeySizeBits);
    }

    private static RSA LoadFromFile(string pemPath, ILogger logger)
    {
        if (!File.Exists(pemPath))
            throw new InvalidOperationException(
                $"{SignerJwtOptions.SectionName}:PrivateKeyPem points to '{pemPath}', but the file does not exist."
            );

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(File.ReadAllText(pemPath));
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                $"{SignerJwtOptions.SectionName}:PrivateKeyPem '{pemPath}' is not a valid RSA private key PEM.",
                ex
            );
        }

        if (!HasPrivateKey(rsa) || rsa.KeySize < MinimumKeySizeBits)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                $"{SignerJwtOptions.SectionName}:PrivateKeyPem '{pemPath}' must be an RSA private key of at least "
                    + $"{MinimumKeySizeBits} bits."
            );
        }

        logger.LogInformation("Loaded Signature signer RSA key from {Path}.", pemPath);
        return rsa;
    }

    // Un PEM con solo la parte pública importa bien pero no sirve para firmar.
    private static bool HasPrivateKey(RSA rsa)
    {
        try
        {
            rsa.ExportParameters(includePrivateParameters: true);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
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
