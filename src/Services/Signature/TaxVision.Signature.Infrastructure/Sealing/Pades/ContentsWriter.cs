using BuildingBlocks.Results;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Escribe el blob CMS DER en hex dentro del placeholder <c>/Contents</c>, respetando
/// exactamente la longitud reservada (padding con ceros). Si el CMS supera el
/// espacio reservado, devuelve <c>Signature.PadesB.ContentsOverflow</c> para que el
/// sealer suba el <c>ContentsReservedBytes</c> antes de reintentar.
/// </summary>
public static class ContentsWriter
{
    private const string HexDigits = "0123456789ABCDEF";

    public static Result Write(byte[] pdf, int offset, int length, byte[] cmsDer)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(cmsDer);
        if (offset < 0 || length <= 0 || offset + length > pdf.Length)
            return Result.Failure(new Error("Signature.PadesB.ContentsAnchor", "Invalid /Contents anchor."));

        var requiredHexChars = cmsDer.Length * 2;
        if (requiredHexChars > length)
            return Result.Failure(
                new Error(
                    "Signature.PadesB.ContentsOverflow",
                    $"CMS blob needs {requiredHexChars} hex chars but /Contents reserves {length}."
                )
            );

        WriteHex(cmsDer, pdf.AsSpan(offset, requiredHexChars));
        PadZeros(pdf.AsSpan(offset + requiredHexChars, length - requiredHexChars));
        return Result.Success();
    }

    private static void WriteHex(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        for (var i = 0; i < source.Length; i++)
        {
            var b = source[i];
            destination[i * 2] = (byte)HexDigits[b >> 4];
            destination[i * 2 + 1] = (byte)HexDigits[b & 0x0F];
        }
    }

    private static void PadZeros(Span<byte> destination) => destination.Fill((byte)'0');
}
