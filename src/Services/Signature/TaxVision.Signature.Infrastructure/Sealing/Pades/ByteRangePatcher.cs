using System.Globalization;
using System.Text;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Infrastructure.Sealing.Pades;

/// <summary>
/// Reescribe el placeholder <c>/ByteRange [0000000000 ...]</c> con los valores
/// definitivos <c>[0 sigStart sigEnd (fileLen-sigEnd)]</c>, respetando el ancho
/// exacto del placeholder (padding con espacios a la derecha).
/// </summary>
public static class ByteRangePatcher
{
    public static Result Patch(
        byte[] pdf,
        int byteRangeOffset,
        int byteRangeLength,
        int contentsOffset,
        int contentsLength
    )
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (byteRangeOffset < 0 || byteRangeLength <= 0)
            return Result.Failure(new Error("Signature.PadesB.InvalidByteRange", "Invalid /ByteRange anchor."));
        if (contentsOffset < 0 || contentsLength <= 0)
            return Result.Failure(new Error("Signature.PadesB.InvalidContents", "Invalid /Contents anchor."));

        var first = 0;
        var firstLen = contentsOffset;
        var second = contentsOffset + contentsLength;
        var secondLen = pdf.Length - second;

        var rendered = $"{first} {firstLen} {second} {secondLen}";
        if (rendered.Length > byteRangeLength)
            return Result.Failure(
                new Error("Signature.PadesB.ByteRangeOverflow", "Computed /ByteRange doesn't fit in placeholder.")
            );

        var padded = rendered.PadRight(byteRangeLength, ' ');
        var bytes = Encoding.Latin1.GetBytes(padded);
        Buffer.BlockCopy(bytes, 0, pdf, byteRangeOffset, bytes.Length);
        return Result.Success();
    }

    /// <summary>Vuelve a parsear el ByteRange que se escribio — util para diagnosticos.</summary>
    public static Result<(int First, int FirstLen, int Second, int SecondLen)> Read(byte[] pdf, int offset, int length)
    {
        var text = Encoding.Latin1.GetString(pdf, offset, length).Trim();
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return Result.Failure<(int, int, int, int)>(
                new Error("Signature.PadesB.ByteRangeUnreadable", "/ByteRange must contain 4 integers.")
            );

        var values = new int[4];
        for (var i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                return Result.Failure<(int, int, int, int)>(
                    new Error("Signature.PadesB.ByteRangeNumber", $"/ByteRange[{i}] is not an integer.")
                );
        return Result.Success((values[0], values[1], values[2], values[3]));
    }
}
