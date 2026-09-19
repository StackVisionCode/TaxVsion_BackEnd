using BuildingBlocks.Results;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// Validación pura del PNG de firma que sube el firmante anónimo. No toca I/O ni storage:
/// inspecciona los bytes para confirmar que es realmente un PNG (magic bytes + chunk IHDR),
/// que su peso está acotado y que sus dimensiones son razonables. Así el endpoint público
/// rechaza payloads maliciosos/enormes antes de tocar MinIO, y la regla queda testeable.
///
/// <para>
/// Solo PNG: el pad del front exporta PNG transparente y el motor de sellado estampa PNG.
/// Un único formato reduce la superficie de ataque (sin decoders de JPEG/GIF/SVG) y mantiene
/// el fondo transparente que la firma necesita.
/// </para>
/// </summary>
public static class SignatureImageValidator
{
    /// <summary>Firma PNG: 8 bytes de cabecera fijos.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Cabecera mínima verificable: 8 (firma) + 4 (len) + 4 ("IHDR") + 8 (ancho+alto).
    private const int MinInspectableLength = 24;

    /// <summary>~1.5 MB — de sobra para una firma manuscrita trazada; corta bombas de descompresión.</summary>
    public const int MaxBytes = 1_500_000;

    private const int MaxDimension = 4000;

    public static Result Validate(byte[]? content)
    {
        if (content is null || content.Length == 0)
            return Result.Failure(new Error("Signature.Image.Empty", "The signature image is empty."));

        if (content.Length > MaxBytes)
            return Result.Failure(
                new Error("Signature.Image.TooLarge", $"The signature image exceeds {MaxBytes} bytes.")
            );

        if (content.Length < MinInspectableLength || !HasPngSignature(content))
            return Result.Failure(new Error("Signature.Image.NotPng", "The signature image must be a PNG."));

        if (!HasIhdrChunk(content))
            return Result.Failure(new Error("Signature.Image.NotPng", "The signature image must be a PNG."));

        var width = ReadBigEndianUInt32(content, 16);
        var height = ReadBigEndianUInt32(content, 20);
        if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension)
            return Result.Failure(
                new Error(
                    "Signature.Image.BadDimensions",
                    $"The signature image dimensions must be 1..{MaxDimension} px."
                )
            );

        return Result.Success();
    }

    private static bool HasPngSignature(byte[] content)
    {
        for (var i = 0; i < PngSignature.Length; i++)
        {
            if (content[i] != PngSignature[i])
                return false;
        }
        return true;
    }

    // El primer chunk de todo PNG válido es IHDR: bytes 12..15 = 'I','H','D','R'.
    private static bool HasIhdrChunk(byte[] content) =>
        content[12] == (byte)'I' && content[13] == (byte)'H' && content[14] == (byte)'D' && content[15] == (byte)'R';

    private static uint ReadBigEndianUInt32(byte[] content, int offset) =>
        ((uint)content[offset] << 24)
        | ((uint)content[offset + 1] << 16)
        | ((uint)content[offset + 2] << 8)
        | content[offset + 3];
}
