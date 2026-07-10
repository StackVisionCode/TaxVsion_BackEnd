using System.Security.Cryptography;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Calcula el SHA-256 sobre los rangos declarados en <c>/ByteRange</c> — todo el PDF
/// excepto el hueco de <c>/Contents</c>. Este es el <c>messageDigest</c> que se firma
/// via CMS/PKCS#7 en PAdES-B.
/// </summary>
public static class PadesDigestComputer
{
    public static Result<byte[]> ComputeSha256(byte[] pdf, int first, int firstLen, int second, int secondLen)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (first < 0 || firstLen < 0 || second < 0 || secondLen < 0)
            return Result.Failure<byte[]>(
                new Error("Signature.PadesB.NegativeRange", "ByteRange values must be non-negative.")
            );
        if ((long)first + firstLen > pdf.Length || (long)second + secondLen > pdf.Length)
            return Result.Failure<byte[]>(
                new Error("Signature.PadesB.RangeOutOfBounds", "ByteRange exceeds PDF length.")
            );

        using var sha = SHA256.Create();
        sha.TransformBlock(pdf, first, firstLen, null, 0);
        sha.TransformFinalBlock(pdf, second, secondLen);
        return Result.Success(sha.Hash ?? Array.Empty<byte>());
    }
}
