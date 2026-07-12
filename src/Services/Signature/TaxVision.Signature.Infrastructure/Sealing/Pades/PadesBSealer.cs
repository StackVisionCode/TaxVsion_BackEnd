using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Infrastructure.Sealing.Cms;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// <see cref="ICmsPdfSigner"/> que produce un PDF con firma PAdES-B nativa. Coordina
/// (1) parseo del trailer del PDF visualmente sellado, (2) append incremental con
/// Signature Dictionary + AcroForm + Catalog, (3) computo del <c>/ByteRange</c>,
/// (4) hash SHA-256 sobre los rangos declarados, (5) firma CMS/PKCS#7 (opcionalmente
/// con timestamp RFC 3161 → PAdES-B-T) y (6) escritura del CMS DER dentro del
/// placeholder <c>/Contents</c>.
///
/// <para>
/// Es sincrono a nivel de <c>ICmsPdfSigner.Sign</c> — el timestamp async se resuelve
/// con <c>GetAwaiter().GetResult()</c> ya que corre en un worker background, no en
/// un endpoint HTTP.
/// </para>
/// </summary>
public sealed class PadesBSealer(
    IOptions<PadesOptions> padesOptions,
    IncrementalSignatureAppender appender,
    PadesCmsSigner cmsSigner,
    ILogger<PadesBSealer> logger
) : ICmsPdfSigner
{
    public CmsSignedPdfResult Sign(byte[] visuallySealedPdfBytes)
    {
        ArgumentNullException.ThrowIfNull(visuallySealedPdfBytes);

        var trailerResult = PdfTrailerParser.Parse(visuallySealedPdfBytes);
        if (trailerResult.IsFailure)
            throw new InvalidOperationException(trailerResult.Error.Message);

        var layout = appender.Append(visuallySealedPdfBytes, trailerResult.Value);
        var pdf = layout.PdfWithPlaceholders;

        var patchResult = ByteRangePatcher.Patch(
            pdf,
            layout.ByteRangeOffset,
            layout.ByteRangeLength,
            layout.ContentsOffset,
            layout.ContentsLength
        );
        if (patchResult.IsFailure)
            throw new InvalidOperationException(patchResult.Error.Message);

        var byteRangeRead = ByteRangePatcher.Read(pdf, layout.ByteRangeOffset, layout.ByteRangeLength);
        if (byteRangeRead.IsFailure)
            throw new InvalidOperationException(byteRangeRead.Error.Message);
        var (first, firstLen, second, secondLen) = byteRangeRead.Value;

        var digestResult = PadesDigestComputer.ComputeSha256(pdf, first, firstLen, second, secondLen);
        if (digestResult.IsFailure)
            throw new InvalidOperationException(digestResult.Error.Message);

        var cmsResult = cmsSigner.SignAsync(digestResult.Value, CancellationToken.None).GetAwaiter().GetResult();
        if (cmsResult.IsFailure)
            throw new InvalidOperationException(cmsResult.Error.Message);
        var cmsBlob = cmsResult.Value;

        var writeResult = ContentsWriter.Write(pdf, layout.ContentsOffset, layout.ContentsLength, cmsBlob);
        if (writeResult.IsFailure)
            throw new InvalidOperationException(writeResult.Error.Message);

        logger.LogInformation(
            "PAdES-B signature emitted: cmsBytes={CmsBytes}, reserved={Reserved}, byteRange=[{A} {B} {C} {D}]",
            cmsBlob.Length,
            padesOptions.Value.ContentsReservedBytes,
            first,
            firstLen,
            second,
            secondLen
        );

        var cmsHash = Convert.ToHexString(SHA256.HashData(cmsBlob)).ToLowerInvariant();
        return new CmsSignedPdfResult(
            SignedPdfBytes: pdf,
            CmsSignatureBytes: cmsBlob,
            SignerCommonName: cmsSigner.GetSignerCommonName(),
            SignatureSha256: cmsHash
        );
    }
}
