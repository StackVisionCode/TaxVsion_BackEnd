using System.Globalization;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.X509;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Aplica el nivel PAdES-B-LT sobre un PDF ya firmado con PAdES-B/B-T: agrega el
/// Document Security Store (DSS) con Certs, CRLs y OCSPs necesarios para validar la
/// firma sin acceso online en el futuro. La adicion es un incremental update que
/// preserva las revisiones anteriores (verificadores siguen viendo la firma
/// original intacta).
///
/// <para>
/// Tolerancia a fallos: si un CRL/OCSP concreto no se puede obtener, se registra en
/// <see cref="ValidationMaterial.Warnings"/> y se continua. El PDF termina con la
/// evidencia parcial y el validador reportara "no CRL/OCSP" para esa cadena.
/// </para>
/// </summary>
public sealed class LongTermValidationEnricher(
    CrlFetcher crlFetcher,
    OcspFetcher ocspFetcher,
    ILogger<LongTermValidationEnricher> logger
)
{
    public async Task<Result<byte[]>> EnrichAsync(
        byte[] signedPdf,
        IReadOnlyList<X509Certificate> chain,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(signedPdf);
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
            return Result.Failure<byte[]>(new Error("Signature.PadesLt.NoChain", "Certificate chain is empty."));

        var material = await CollectValidationMaterialAsync(chain, ct);
        foreach (var warning in material.Warnings)
            logger.LogWarning("PAdES-B-LT enrichment warning: {Warning}", warning);

        var trailer = PdfTrailerParser.Parse(signedPdf);
        if (trailer.IsFailure)
            return Result.Failure<byte[]>(trailer.Error);

        var writer = new DssWriter(signedPdf, trailer.Value);
        writer.WriteDssIncrementalUpdate(material);
        return Result.Success(writer.ToArray());
    }

    private async Task<ValidationMaterial> CollectValidationMaterialAsync(
        IReadOnlyList<X509Certificate> chain,
        CancellationToken ct
    )
    {
        var material = new ValidationMaterial();
        material.Certificates.AddRange(chain);

        for (var i = 0; i < chain.Count; i++)
        {
            var certificate = chain[i];
            var issuer = i + 1 < chain.Count ? chain[i + 1] : certificate;

            try
            {
                var crls = await crlFetcher.FetchAsync(certificate, ct);
                if (crls.Count == 0)
                    material.Warnings.Add($"No CRL retrieved for {certificate.SubjectDN}");
                material.Crls.AddRange(crls);
            }
            catch (Exception ex)
            {
                material.Warnings.Add($"CRL fetch failed for {certificate.SubjectDN}: {ex.Message}");
            }

            try
            {
                var ocsp = await ocspFetcher.FetchAsync(certificate, issuer, ct);
                if (ocsp is null)
                    material.Warnings.Add($"No OCSP retrieved for {certificate.SubjectDN}");
                else
                    material.Ocsps.Add(ocsp);
            }
            catch (Exception ex)
            {
                material.Warnings.Add($"OCSP fetch failed for {certificate.SubjectDN}: {ex.Message}");
            }
        }

        return material;
    }

    // ------------------------------------------------------------------

    private sealed class DssWriter(byte[] originalPdf, PdfTrailerInfo trailer)
    {
        private readonly MemoryStream _stream = InitStream(originalPdf, trailer);
        private static readonly Dictionary<string, int> _emptyMap = new();

        public void WriteDssIncrementalUpdate(ValidationMaterial material)
        {
            var baseObj = trailer.PrevSize > 0 ? (int)trailer.PrevSize : 200;
            var certOffsets = WriteStreams(baseObj, material.Certificates.Select(c => c.GetEncoded()).ToList());
            var crlOffsets = WriteStreams(baseObj + certOffsets.Count, material.Crls);
            var ocspOffsets = WriteStreams(baseObj + certOffsets.Count + crlOffsets.Count, material.Ocsps);

            var dssObj = baseObj + certOffsets.Count + crlOffsets.Count + ocspOffsets.Count;
            var dssOffset = (int)_stream.Position;
            WriteDssDictionary(dssObj, certOffsets, crlOffsets, ocspOffsets);

            var catalogObj = dssObj + 1;
            var catalogOffset = (int)_stream.Position;
            WriteAscii($"{catalogObj} 0 obj\n<<\n/Type /Catalog\n/DSS {dssObj} 0 R\n>>\nendobj\n");

            var xrefOffset = (int)_stream.Position;
            var allOffsets = new List<int>();
            allOffsets.AddRange(certOffsets.Values);
            allOffsets.AddRange(crlOffsets.Values);
            allOffsets.AddRange(ocspOffsets.Values);
            allOffsets.Add(dssOffset);
            allOffsets.Add(catalogOffset);

            WriteAscii($"xref\n{baseObj} {allOffsets.Count}\n");
            foreach (var offset in allOffsets)
                WriteAscii($"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");

            var newSize = catalogObj + 1;
            WriteAscii(
                $"trailer\n<<\n/Size {newSize}\n/Prev {trailer.StartXref}\n/Root {catalogObj} 0 R\n>>\nstartxref\n{xrefOffset}\n%%EOF\n"
            );
        }

        public byte[] ToArray() => _stream.ToArray();

        private Dictionary<int, int> WriteStreams(int startObject, IReadOnlyList<byte[]> blobs)
        {
            var offsets = new Dictionary<int, int>();
            for (var i = 0; i < blobs.Count; i++)
            {
                var objNumber = startObject + i;
                var offset = (int)_stream.Position;
                offsets[objNumber] = offset;
                WriteAscii($"{objNumber} 0 obj\n<< /Length {blobs[i].Length} >>\nstream\n");
                _stream.Write(blobs[i], 0, blobs[i].Length);
                WriteAscii("\nendstream\nendobj\n");
            }
            return offsets;
        }

        private void WriteDssDictionary(
            int objNumber,
            Dictionary<int, int> certObjs,
            Dictionary<int, int> crlObjs,
            Dictionary<int, int> ocspObjs
        )
        {
            WriteAscii($"{objNumber} 0 obj\n<<\n");
            if (certObjs.Count > 0)
                WriteAscii($"/Certs [{string.Join(' ', certObjs.Keys.Select(k => $"{k} 0 R"))}]\n");
            if (crlObjs.Count > 0)
                WriteAscii($"/CRLs [{string.Join(' ', crlObjs.Keys.Select(k => $"{k} 0 R"))}]\n");
            if (ocspObjs.Count > 0)
                WriteAscii($"/OCSPs [{string.Join(' ', ocspObjs.Keys.Select(k => $"{k} 0 R"))}]\n");
            WriteAscii(">>\nendobj\n");
        }

        private void WriteAscii(string s)
        {
            var bytes = Encoding.Latin1.GetBytes(s);
            _stream.Write(bytes, 0, bytes.Length);
        }

        private static MemoryStream InitStream(byte[] pdf, PdfTrailerInfo trailer)
        {
            var stream = new MemoryStream(pdf.Length + 128 * 1024);
            stream.Write(pdf, 0, (int)trailer.EofOffset);
            var pos = stream.Position;
            if (pos == 0)
                return stream;
            var last = stream.GetBuffer()[pos - 1];
            if (last is not ((byte)'\n' or (byte)'\r'))
                stream.WriteByte((byte)'\n');
            return stream;
        }
    }
}
