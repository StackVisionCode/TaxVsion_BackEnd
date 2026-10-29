using System.Buffers.Binary;

namespace BuildingBlocks.Imaging;

/// <summary>
/// Detecta animación mirando solo los encabezados. Hace falta para APNG: el decodificador PNG de Skia
/// (libpng) ignora <c>acTL</c> y reporta un solo frame. WebP se revisa también aquí (bit de animación
/// de VP8X) como respaldo del <c>FrameCount</c> del codec.
/// </summary>
internal static class AnimationSniffer
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsAnimated(ReadOnlySpan<byte> data) => IsAnimatedPng(data) || IsAnimatedWebp(data);

    private static bool IsAnimatedPng(ReadOnlySpan<byte> data)
    {
        if (!data.StartsWith(PngSignature))
            return false;

        // acTL debe ir antes del primer IDAT; se recorren los chunks hasta ahí.
        long offset = PngSignature.Length;
        while (offset + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice((int)offset, 4));
            var type = data.Slice((int)offset + 4, 4);
            if (type.SequenceEqual("acTL"u8))
                return true;
            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
                return false;
            offset += 12L + length;
        }
        return false;
    }

    private static bool IsAnimatedWebp(ReadOnlySpan<byte> data)
    {
        // RIFF....WEBPVP8X + 4 bytes de tamaño + flags (bit 1 = animación).
        const int flagsOffset = 20;
        if (data.Length <= flagsOffset)
            return false;
        if (!data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WEBP"u8))
            return false;
        if (!data.Slice(12, 4).SequenceEqual("VP8X"u8))
            return false;
        return (data[flagsOffset] & 0x02) != 0;
    }
}
