using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace TaxVision.BuildingBlocks.Tests.Imaging;

/// <summary>
/// Fixtures generadas en memoria, deterministas y sin binarios en el repo. Las que SkiaSharp no sabe
/// codificar (EXIF, APNG, WebP animado, GIF, BMP, HEIC, encabezados gigantes) se arman a mano byte a byte
/// siguiendo cada especificación.
/// </summary>
internal static class AvatarTestImages
{
    public static readonly SKColor Red = new(220, 20, 20);
    public static readonly SKColor Green = new(20, 200, 20);
    public static readonly SKColor Blue = new(20, 20, 220);
    public static readonly SKColor Yellow = new(230, 220, 20);

    /// <summary>Marcador ASCII que se mete en el EXIF para comprobar que no sobrevive.</summary>
    public const string ExifSecret = "TAXVISION-GPS-SECRET-40.4461N";

    /// <summary>GPSLatitude 46.12 en rational little-endian (4612/100): no debe aparecer en la salida.</summary>
    public static readonly byte[] GpsLatitudeSeconds = [0x04, 0x12, 0x00, 0x00, 0x64, 0x00, 0x00, 0x00];

    // ------------------------------------------------------------------ generadas con SkiaSharp

    public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 90)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    public static byte[] EncodeLosslessWebp(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels();
        using var data =
            pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 75))
            ?? throw new InvalidOperationException("Could not encode lossless WebP.");
        return data.ToArray();
    }

    public static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(color);
        return bitmap;
    }

    /// <summary>Cuatro cuadrantes: arriba-izquierda rojo, arriba-derecha verde, abajo-izquierda azul,
    /// abajo-derecha amarillo. Sirve para saber hacia dónde quedó rotada la imagen.</summary>
    public static SKBitmap Quadrants(int side)
    {
        var bitmap = Solid(side, side, SKColors.Black);
        using var canvas = new SKCanvas(bitmap);
        var half = side / 2f;
        Fill(canvas, Red, 0, 0, half, half);
        Fill(canvas, Green, half, 0, side, half);
        Fill(canvas, Blue, 0, half, half, side);
        Fill(canvas, Yellow, half, half, side, side);
        return bitmap;
    }

    /// <summary>Fondo rojo, banda verde en el cuadrado central y un cuadro azul justo en el centro.
    /// Tras el recorte al centro no debe quedar nada rojo.</summary>
    public static SKBitmap CenterTarget(int width, int height)
    {
        var bitmap = Solid(width, height, Red);
        using var canvas = new SKCanvas(bitmap);
        var side = Math.Min(width, height);
        float left = (width - side) / 2f;
        float top = (height - side) / 2f;
        Fill(canvas, Green, left, top, left + side, top + side);
        var third = side / 3f;
        Fill(canvas, Blue, left + third, top + third, left + 2 * third, top + 2 * third);
        return bitmap;
    }

    /// <summary>Degradado suave con algunas formas: se parece más a una foto que un color plano.</summary>
    public static SKBitmap PhotoLike(int width, int height)
    {
        var bitmap = Solid(width, height, SKColors.White);
        using var canvas = new SKCanvas(bitmap);
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            [new SKColor(30, 60, 140), new SKColor(240, 180, 120), new SKColor(60, 160, 90)],
            SKShaderTileMode.Clamp
        );
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);
        using var circle = new SKPaint { Color = new SKColor(250, 230, 210), IsAntialias = true };
        canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 4f, circle);
        return bitmap;
    }

    private static void Fill(SKCanvas canvas, SKColor color, float left, float top, float right, float bottom)
    {
        using var paint = new SKPaint { Color = color };
        canvas.DrawRect(SKRect.Create(left, top, right - left, bottom - top), paint);
    }

    // ------------------------------------------------------------------ EXIF inyectado

    /// <summary>
    /// Inserta tras el SOI un segmento APP1 "Exif" con IFD0 (Orientation + ImageDescription con
    /// <see cref="ExifSecret"/>) y un GPS IFD (versión, latitud N 40° 26' 46.12"). Es lo que deja un
    /// teléfono, armado a mano para no versionar un binario.
    /// </summary>
    public static byte[] WithExif(byte[] jpeg, ushort orientation)
    {
        if (jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            throw new ArgumentException("Not a JPEG.", nameof(jpeg));

        var description = Encoding.ASCII.GetBytes(ExifSecret + "\0");
        var descriptionLength = description.Length + (description.Length % 2);

        const int ifd0Offset = 8;
        const int ifd0Size = 2 + 3 * 12 + 4;
        var descriptionOffset = ifd0Offset + ifd0Size;
        var gpsOffset = descriptionOffset + descriptionLength;
        const int gpsSize = 2 + 3 * 12 + 4;
        var latitudeOffset = gpsOffset + gpsSize;
        var tiff = new byte[latitudeOffset + 24];

        // Encabezado TIFF little-endian.
        "II"u8.CopyTo(tiff);
        WriteU16(tiff, 2, 42);
        WriteU32(tiff, 4, ifd0Offset);

        // IFD0: Orientation, ImageDescription, puntero al GPS IFD.
        var p = ifd0Offset;
        WriteU16(tiff, p, 3);
        p += 2;
        p = WriteEntry(tiff, p, tag: 0x0112, type: 3, count: 1, value: orientation);
        p = WriteEntry(tiff, p, tag: 0x010E, type: 2, count: (uint)description.Length, value: (uint)descriptionOffset);
        p = WriteEntry(tiff, p, tag: 0x8825, type: 4, count: 1, value: (uint)gpsOffset);
        WriteU32(tiff, p, 0);
        description.CopyTo(tiff, descriptionOffset);

        // GPS IFD: GPSVersionID 2.2.0.0, GPSLatitudeRef "N", GPSLatitude (3 rationals).
        p = gpsOffset;
        WriteU16(tiff, p, 3);
        p += 2;
        p = WriteEntry(tiff, p, tag: 0x0000, type: 1, count: 4, value: 0x00000202);
        p = WriteEntry(tiff, p, tag: 0x0001, type: 2, count: 2, value: 'N');
        p = WriteEntry(tiff, p, tag: 0x0002, type: 5, count: 3, value: (uint)latitudeOffset);
        WriteU32(tiff, p, 0);
        WriteU32(tiff, latitudeOffset, 40);
        WriteU32(tiff, latitudeOffset + 4, 1);
        WriteU32(tiff, latitudeOffset + 8, 26);
        WriteU32(tiff, latitudeOffset + 12, 1);
        GpsLatitudeSeconds.CopyTo(tiff, latitudeOffset + 16);

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var segmentLength = payload.Length + 2;
        var app1 = new byte[4 + payload.Length];
        app1[0] = 0xFF;
        app1[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(app1.AsSpan(2), (ushort)segmentLength);
        payload.CopyTo(app1, 4);

        return [.. jpeg.AsSpan(0, 2), .. app1, .. jpeg.AsSpan(2)];
    }

    private static int WriteEntry(byte[] buffer, int offset, ushort tag, ushort type, uint count, uint value)
    {
        WriteU16(buffer, offset, tag);
        WriteU16(buffer, offset + 2, type);
        WriteU32(buffer, offset + 4, count);
        WriteU32(buffer, offset + 8, value);
        return offset + 12;
    }

    private static void WriteU16(byte[] buffer, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), value);

    private static void WriteU32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);

    // ------------------------------------------------------------------ PNG a mano

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>PNG válido de encabezado (IHDR con CRC correcto) que declara <paramref name="width"/>×<paramref name="height"/>
    /// pero trae un IDAT vacío: simula una bomba de descompresión sin reservar la imagen real.</summary>
    public static byte[] PngHeaderClaiming(int width, int height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // profundidad
        ihdr[9] = 6; // RGBA
        // zlib con un bloque stored vacío.
        byte[] idat = [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];
        return [.. PngSignature, .. Chunk("IHDR", ihdr), .. Chunk("IDAT", idat), .. Chunk("IEND", [])];
    }

    /// <summary>Convierte un PNG en APNG declarando <c>acTL</c> (2 frames) justo después del IHDR.</summary>
    public static byte[] WithApngControl(byte[] png)
    {
        const int afterIhdr = 8 + 25;
        var actl = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl, 2);
        BinaryPrimitives.WriteUInt32BigEndian(actl.AsSpan(4), 0);
        return [.. png.AsSpan(0, afterIhdr), .. Chunk("acTL", actl), .. png.AsSpan(afterIhdr)];
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        typeBytes.CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32([.. typeBytes, .. data]));
        return chunk;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }

    // ------------------------------------------------------------------ WebP animado a mano

    /// <summary>
    /// WebP animado (VP8X con bit de animación + ANIM + N frames ANMF) cuyos frames reutilizan el
    /// bitstream VP8L de un WebP sin pérdida generado por SkiaSharp.
    /// </summary>
    public static byte[] AnimatedWebp(int side, int frames)
    {
        using var bitmap = Solid(side, side, Green);
        var still = EncodeLosslessWebp(bitmap);
        var vp8l = FindRiffChunk(still, "VP8L");

        var vp8x = new byte[10];
        vp8x[0] = 0x02 | 0x10; // animación + alfa
        WriteU24(vp8x, 4, side - 1);
        WriteU24(vp8x, 7, side - 1);

        var anim = new byte[6]; // fondo transparente, loop infinito

        var body = new List<byte>();
        body.AddRange("WEBP"u8.ToArray());
        body.AddRange(RiffChunk("VP8X", vp8x));
        body.AddRange(RiffChunk("ANIM", anim));
        for (var i = 0; i < frames; i++)
        {
            var header = new byte[16];
            WriteU24(header, 6, side - 1);
            WriteU24(header, 9, side - 1);
            WriteU24(header, 12, 100); // 100 ms
            body.AddRange(RiffChunk("ANMF", [.. header, .. vp8l]));
        }

        var riff = new byte[8 + body.Count];
        "RIFF"u8.CopyTo(riff);
        BinaryPrimitives.WriteUInt32LittleEndian(riff.AsSpan(4), (uint)body.Count);
        body.CopyTo(riff, 8);
        return riff;
    }

    /// <summary>Lista los fourcc de los chunks de primer nivel de un RIFF/WebP.</summary>
    public static IReadOnlyList<string> RiffChunkTypes(byte[] webp)
    {
        var types = new List<string>();
        var offset = 12;
        while (offset + 8 <= webp.Length)
        {
            types.Add(Encoding.ASCII.GetString(webp, offset, 4));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(offset + 4));
            offset += 8 + size + (size % 2);
        }
        return types;
    }

    /// <summary>Devuelve el chunk completo (fourcc + tamaño + datos + relleno).</summary>
    private static byte[] FindRiffChunk(byte[] webp, string type)
    {
        var offset = 12;
        while (offset + 8 <= webp.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(offset + 4));
            var total = 8 + size + (size % 2);
            if (Encoding.ASCII.GetString(webp, offset, 4) == type)
                return webp.AsSpan(offset, total).ToArray();
            offset += total;
        }
        throw new InvalidOperationException($"Chunk {type} not found.");
    }

    private static byte[] RiffChunk(string type, byte[] data)
    {
        var chunk = new byte[8 + data.Length + (data.Length % 2)];
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)data.Length);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private static void WriteU24(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
    }

    // ------------------------------------------------------------------ formatos rechazados

    /// <summary>GIF89a de 1×1 (el clásico pixel transparente).</summary>
    public static readonly byte[] Gif1X1 =
    [
        0x47,
        0x49,
        0x46,
        0x38,
        0x39,
        0x61,
        0x01,
        0x00,
        0x01,
        0x00,
        0x80,
        0x00,
        0x00,
        0xFF,
        0xFF,
        0xFF,
        0x00,
        0x00,
        0x00,
        0x21,
        0xF9,
        0x04,
        0x01,
        0x00,
        0x00,
        0x00,
        0x00,
        0x2C,
        0x00,
        0x00,
        0x00,
        0x00,
        0x01,
        0x00,
        0x01,
        0x00,
        0x00,
        0x02,
        0x02,
        0x44,
        0x01,
        0x00,
        0x3B,
    ];

    /// <summary>BMP de 24 bits sin compresión, todo rojo.</summary>
    public static byte[] Bmp(int width, int height)
    {
        var rowSize = (width * 3 + 3) & ~3;
        var pixels = rowSize * height;
        var bmp = new byte[54 + pixels];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteU32(bmp, 2, (uint)bmp.Length);
        WriteU32(bmp, 10, 54);
        WriteU32(bmp, 14, 40);
        WriteU32(bmp, 18, (uint)width);
        WriteU32(bmp, 22, (uint)height);
        WriteU16(bmp, 26, 1);
        WriteU16(bmp, 28, 24);
        WriteU32(bmp, 34, (uint)pixels);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            bmp[54 + y * rowSize + x * 3 + 2] = 0xFF;
        return bmp;
    }

    /// <summary>Caja <c>ftyp</c> de HEIC (lo que manda un iPhone) seguida de relleno.</summary>
    public static byte[] HeicHeader() =>
        [0x00, 0x00, 0x00, 0x18, .. "ftypheic"u8, 0x00, 0x00, 0x00, 0x00, .. "mif1heic"u8, .. new byte[256]];

    public static byte[] Svg() =>
        Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\" width=\"512\" height=\"512\">"
                + "<script>alert(document.cookie)</script><rect width=\"512\" height=\"512\" fill=\"red\"/></svg>"
        );

    // ------------------------------------------------------------------ lectura de la salida

    public static SKBitmap Decode(byte[] encoded) =>
        SKBitmap.Decode(encoded) ?? throw new InvalidOperationException("Output is not decodable.");
}
