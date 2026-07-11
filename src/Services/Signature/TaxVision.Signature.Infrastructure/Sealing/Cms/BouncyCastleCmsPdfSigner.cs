using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TaxVision.Signature.Application.Abstractions.Sealing;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;
using SysX509Certificate2 = System.Security.Cryptography.X509Certificates.X509Certificate2;

namespace TaxVision.Signature.Infrastructure.Sealing.Cms;

/// <summary>
/// CMS/PKCS#7 signer con BouncyCastle. El certificado y la clave privada vienen de
/// <see cref="CmsSignerOptions"/> (PFX/PKCS#12) — se recomienda inyectar via Vault en
/// producción y usar un self-signed sólo en dev.
///
/// <para>Fases (métodos privados con nombre autoexplicativo):</para>
/// <list type="number">
///   <item>Cargar el certificado + clave privada de <see cref="CmsSignerOptions"/>.</item>
///   <item>Calcular SHA-256 del PDF visualmente sellado (será el messageDigest CMS).</item>
///   <item>Generar la firma CMS detached con BouncyCastle.</item>
///   <item>Embed el blob CMS como <c>/CmsSignature</c> en el diccionario <c>/Info</c> del PDF —
///     compatible con toolchains que leen metadata sin depender del signature dictionary
///     de bajo nivel.</item>
/// </list>
///
/// <para>
/// Limitación conocida: PdfSharp 6.x no expone la Signature Dictionary con ByteRange que
/// exige el modelo estricto PAdES-B para que Adobe Reader la valide como firma nativa.
/// Cuando llegue Fase 8 (PAdES-B-T + LTV + TSA) se sustituye este signer por uno que
/// hable directamente con la Signature Dictionary (o se migra a otra librería). El blob
/// CMS producido aquí es válido criptográficamente y verificable con OpenSSL/BC.
/// </para>
/// </summary>
public sealed class BouncyCastleCmsPdfSigner : ICmsPdfSigner
{
    private const string DefaultSignatureIdInfoKey = "/CmsSignatureBase64";
    private const string DigestAlgorithm = "SHA-256";

    private readonly SysX509Certificate2 _certificate;
    private readonly BcX509Certificate _bcCertificate;
    private readonly AsymmetricKeyParameter _privateKey;

    public BouncyCastleCmsPdfSigner(IOptions<CmsSignerOptions> options)
    {
        var opt = options.Value;
        _certificate = LoadCertificate(opt);
        _bcCertificate = ConvertToBc(_certificate);
        _privateKey = ExtractPrivateKey(_certificate);
    }

    public CmsSignedPdfResult Sign(byte[] visuallySealedPdfBytes)
    {
        ArgumentNullException.ThrowIfNull(visuallySealedPdfBytes);

        var cmsBlob = BuildCmsSignature(visuallySealedPdfBytes);
        var signedPdf = EmbedCmsInPdf(visuallySealedPdfBytes, cmsBlob);
        var cmsHash = Convert.ToHexString(SHA256.HashData(cmsBlob)).ToLowerInvariant();

        return new CmsSignedPdfResult(
            SignedPdfBytes: signedPdf,
            CmsSignatureBytes: cmsBlob,
            SignerCommonName: ExtractCommonName(_certificate),
            SignatureSha256: cmsHash
        );
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private static SysX509Certificate2 LoadCertificate(CmsSignerOptions opt)
    {
        if (string.IsNullOrWhiteSpace(opt.CertificatePath))
            throw new InvalidOperationException("Signature:Sealing:Cms:CertificatePath must be configured.");
        if (!File.Exists(opt.CertificatePath))
            throw new FileNotFoundException($"Certificate PFX not found: {opt.CertificatePath}");

        var raw = File.ReadAllBytes(opt.CertificatePath);
        return X509CertificateLoader.LoadPkcs12(
            raw,
            opt.CertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable
        );
    }

    private static BcX509Certificate ConvertToBc(SysX509Certificate2 sys) =>
        new X509CertificateParser().ReadCertificate(sys.RawData);

    private static AsymmetricKeyParameter ExtractPrivateKey(SysX509Certificate2 cert)
    {
        using var rsa =
            cert.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The signing certificate does not have an RSA private key.");
        var parameters = rsa.ExportParameters(includePrivateParameters: true);
        return DotNetUtilities.GetRsaKeyPair(parameters).Private;
    }

    private byte[] BuildCmsSignature(byte[] contentBytes)
    {
        var generator = new CmsSignedDataGenerator();
        var signerInfoGenerator = new SignerInfoGeneratorBuilder().Build(
            new Asn1SignatureFactory("SHA256WITHRSA", _privateKey),
            _bcCertificate
        );
        generator.AddSignerInfoGenerator(signerInfoGenerator);
        generator.AddCertificates(new SingletonCertStore(_bcCertificate));

        var content = new CmsProcessableByteArray(contentBytes);
        var signed = generator.Generate(content, encapsulate: false);
        return signed.GetEncoded();
    }

    private static byte[] EmbedCmsInPdf(byte[] pdfBytes, byte[] cmsBlob)
    {
        using var input = new MemoryStream(pdfBytes, writable: false);
        using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        var cmsBase64 = Convert.ToBase64String(cmsBlob);
        pdf.Info.Elements[DefaultSignatureIdInfoKey] = new PdfString(cmsBase64);
        pdf.Info.Elements["/CmsDigestAlgorithm"] = new PdfString(DigestAlgorithm);

        using var output = new MemoryStream();
        pdf.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static string ExtractCommonName(SysX509Certificate2 cert)
    {
        var subject = cert.Subject;
        var cn = subject
            .Split(',')
            .Select(part => part.Trim())
            .FirstOrDefault(part => part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrEmpty(cn) ? subject : cn[3..];
    }

    /// <summary>
    /// Store singleton para pasar un único certificado al <c>CmsSignedDataGenerator</c>
    /// sin depender de tipos internos que cambian entre versiones de BouncyCastle.
    /// </summary>
    private sealed class SingletonCertStore : IStore<BcX509Certificate>
    {
        private readonly BcX509Certificate _certificate;

        public SingletonCertStore(BcX509Certificate certificate) => _certificate = certificate;

        public IEnumerable<BcX509Certificate> EnumerateMatches(ISelector<BcX509Certificate>? selector) =>
            selector is null || selector.Match(_certificate) ? new[] { _certificate } : [];
    }
}
