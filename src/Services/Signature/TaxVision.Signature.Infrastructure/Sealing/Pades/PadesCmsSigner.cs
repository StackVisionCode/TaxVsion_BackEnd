using System.Security.Cryptography.X509Certificates;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Collections;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Infrastructure.Sealing.Cms;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;
using SysX509Certificate2 = System.Security.Cryptography.X509Certificates.X509Certificate2;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// CMS/PKCS#7 detached signer alimentado por un <c>messageDigest</c> SHA-256 que ya
/// se computo sobre el <c>/ByteRange</c> del PDF. Devuelve el blob DER que se escribe
/// dentro del placeholder <c>/Contents</c>.
///
/// <para>
/// Si hay un <see cref="ITimestampAuthorityClient"/> configurado, agrega el token
/// RFC 3161 como <c>unsignedAttribute</c> (id-aa-timeStampToken,
/// OID 1.2.840.113549.1.9.16.2.14) — con eso el PDF pasa de PAdES-B a PAdES-B-T.
/// </para>
/// </summary>
public sealed class PadesCmsSigner
{
    private const string SigningAlgorithm = "SHA256WITHRSA";
    private static readonly DerObjectIdentifier IdAaTimeStampToken = new("1.2.840.113549.1.9.16.2.14");

    private readonly SysX509Certificate2 _certificate;
    private readonly BcX509Certificate _bcCertificate;
    private readonly AsymmetricKeyParameter _privateKey;
    private readonly ITimestampAuthorityClient? _tsaClient;

    public PadesCmsSigner(IOptions<CmsSignerOptions> options, ITimestampAuthorityClient? tsaClient = null)
    {
        var opt = options.Value;
        _certificate = LoadCertificate(opt);
        _bcCertificate = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(_certificate.RawData);
        _privateKey = ExtractPrivateKey(_certificate);
        _tsaClient = tsaClient;
    }

    public async Task<Result<byte[]>> SignAsync(byte[] messageDigestSha256, CancellationToken ct = default)
    {
        if (messageDigestSha256 is not { Length: 32 })
            return Result.Failure<byte[]>(
                new Error("Signature.PadesB.DigestSize", "messageDigest must be 32 bytes (SHA-256).")
            );

        var generator = BuildGenerator();
        var content = new CmsProcessableByteArray(messageDigestSha256);
        var signedData = generator.Generate(content, encapsulate: false);

        if (_tsaClient is null)
            return Result.Success(signedData.GetEncoded());

        var timestamped = await AddTimestampAttributeAsync(signedData, ct);
        return timestamped;
    }

    /// <summary>Common Name del certificado — util para el read model.</summary>
    public string GetSignerCommonName()
    {
        var subject = _certificate.Subject;
        var cn = subject
            .Split(',')
            .Select(part => part.Trim())
            .FirstOrDefault(part => part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(cn) ? subject : cn[3..];
    }

    // ------------------------------------------------------------------

    private CmsSignedDataGenerator BuildGenerator()
    {
        var generator = new CmsSignedDataGenerator();
        var signerInfoGenerator = new SignerInfoGeneratorBuilder().Build(
            new Asn1SignatureFactory(SigningAlgorithm, _privateKey),
            _bcCertificate
        );
        generator.AddSignerInfoGenerator(signerInfoGenerator);
        generator.AddCertificates(new SingletonStore(_bcCertificate));
        return generator;
    }

    private async Task<Result<byte[]>> AddTimestampAttributeAsync(CmsSignedData signedData, CancellationToken ct)
    {
        var signerInformationStore = signedData.GetSignerInfos();
        var updatedSigners = new List<SignerInformation>();
        foreach (var signer in signerInformationStore.GetSigners().Cast<SignerInformation>())
        {
            // RFC 3161 § 2.4.1: the TSA is given the SHA-256 hash of the data to be
            // timestamped, not the data itself. For a PAdES-B-T signature-timestamp
            // attribute, that data is the CMS signer's SignatureValue (the raw RSA
            // signature bytes), which for a 2048-bit key is 256 bytes.
            var signatureBytes = signer.GetSignature();
            var signatureDigest = System.Security.Cryptography.SHA256.HashData(signatureBytes);
            var tsResult = await _tsaClient!.RequestTimestampAsync(signatureDigest, ct);
            if (tsResult.IsFailure)
                return Result.Failure<byte[]>(tsResult.Error);

            var attribute = new Org.BouncyCastle.Asn1.Cms.Attribute(
                IdAaTimeStampToken,
                new DerSet(Asn1Object.FromByteArray(tsResult.Value.TokenDer))
            );
            var unsigned = new Org.BouncyCastle.Asn1.Cms.AttributeTable(new DerSet(attribute));
            updatedSigners.Add(SignerInformation.ReplaceUnsignedAttributes(signer, unsigned));
        }

        var replaced = CmsSignedData.ReplaceSigners(signedData, new SignerInformationStore(updatedSigners));
        return Result.Success(replaced.GetEncoded());
    }

    private static SysX509Certificate2 LoadCertificate(CmsSignerOptions opt)
    {
        if (string.IsNullOrWhiteSpace(opt.CertificatePath))
            throw new InvalidOperationException("Signature:Sealing:Cms:CertificatePath must be configured.");
        if (!File.Exists(opt.CertificatePath))
            throw new FileNotFoundException($"Certificate PFX not found: {opt.CertificatePath}");
        var raw = File.ReadAllBytes(opt.CertificatePath);
        // Exportable is required because ExtractPrivateKey() below calls rsa.ExportParameters(true)
        // to hand the key to BouncyCastle. Without this flag, Windows CNG loads the key as
        // non-exportable and ExportParameters throws "The requested operation is not supported."
        return X509CertificateLoader.LoadPkcs12(
            raw,
            opt.CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable
        );
    }

    private static AsymmetricKeyParameter ExtractPrivateKey(SysX509Certificate2 cert)
    {
        using var rsa =
            cert.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The signing certificate does not have an RSA private key.");
        var parameters = rsa.ExportParameters(includePrivateParameters: true);
        return DotNetUtilities.GetRsaKeyPair(parameters).Private;
    }

    private sealed class SingletonStore(BcX509Certificate certificate) : IStore<BcX509Certificate>
    {
        public IEnumerable<BcX509Certificate> EnumerateMatches(ISelector<BcX509Certificate>? selector) =>
            selector is null || selector.Match(certificate) ? new[] { certificate } : [];
    }
}
