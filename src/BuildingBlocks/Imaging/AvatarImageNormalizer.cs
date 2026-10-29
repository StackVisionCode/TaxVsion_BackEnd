using System.Buffers;
using System.Security.Cryptography;
using BuildingBlocks.Results;
using SkiaSharp;

namespace BuildingBlocks.Imaging;

/// <summary>
/// Implementación con SkiaSharp. Sin estado y sin I/O fuera de los streams: es seguro como singleton.
/// Orden de los guardrails: tamaño en bytes → encabezado (formato real, dimensiones, animación) →
/// decodificación → orientación EXIF → recorte cuadrado → reducción → WebP sin metadatos.
/// </summary>
public sealed class AvatarImageNormalizer : IAvatarImageNormalizer
{
    public const string OutputContentType = "image/webp";

    private const int ReadChunkSize = 81_920;

    private readonly AvatarImageOptions _options;

    public AvatarImageNormalizer(AvatarImageOptions? options = null)
    {
        _options = options ?? new AvatarImageOptions();
        _options.Validate();
    }

    public async Task<Result<NormalizedAvatar>> NormalizeAsync(
        Stream input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(input);

        var buffer = await ReadBoundedAsync(input, _options.MaxInputBytes, cancellationToken);
        return buffer is null ? Result.Failure<NormalizedAvatar>(AvatarImageErrors.TooLarge) : Normalize(buffer);
    }

    public Result<NormalizedAvatar> Normalize(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty)
            return Result.Failure<NormalizedAvatar>(AvatarImageErrors.Empty);
        if (input.Length > _options.MaxInputBytes)
            return Result.Failure<NormalizedAvatar>(AvatarImageErrors.TooLarge);

        using var data = SKData.CreateCopy(input);
        // El codec solo lee el encabezado: aquí todavía no se decodificó ningún píxel.
        using var codec = SKCodec.Create(new SKMemoryStream(data), out var createResult);
        if (codec is null)
        {
            // Unimplemented = ningún decodificador reconoce la firma (SVG, HEIC, texto, basura).
            return Result.Failure<NormalizedAvatar>(
                createResult == SKCodecResult.Unimplemented
                    ? AvatarImageErrors.UnsupportedFormat
                    : AvatarImageErrors.Corrupt
            );
        }

        // Formato decidido por el codec (bytes reales), nunca por la extensión ni el Content-Type.
        if (
            codec.EncodedFormat
            is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp)
        )
            return Result.Failure<NormalizedAvatar>(AvatarImageErrors.UnsupportedFormat);

        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (width <= 0 || height <= 0)
            return Result.Failure<NormalizedAvatar>(AvatarImageErrors.Corrupt);

        // Bomba de descompresión: se rechaza con las dimensiones declaradas, antes de reservar memoria.
        if (width > _options.MaxSide || height > _options.MaxSide || (long)width * height > _options.MaxPixels)
            return Result.Failure<NormalizedAvatar>(AvatarImageErrors.TooManyPixels);

        if (codec.FrameCount > 1 || AnimationSniffer.IsAnimated(input))
            return Result.Failure<NormalizedAvatar>(AvatarImageErrors.Animated);

        if (Math.Min(width, height) < _options.MinSide)
            return Result.Failure<NormalizedAvatar>(AvatarImageErrors.TooSmall);

        using var decoded = Decode(codec);
        if (decoded is null)
            return Result.Failure<NormalizedAvatar>(AvatarImageErrors.Corrupt);

        using var oriented = ApplyOrigin(decoded, codec.EncodedOrigin);
        using var square = CropAndResize(oriented ?? decoded);
        var content = EncodeWebp(square);

        return Result.Success(
            new NormalizedAvatar(
                content,
                OutputContentType,
                square.Width,
                square.Height,
                Convert.ToHexStringLower(SHA256.HashData(content))
            )
        );
    }

    /// <summary>Decodifica a RGBA en sRGB (convierte perfiles ICC tipo Display P3). Si el codec sabe
    /// decodificar reducido (JPEG/WebP) y el lado corto sigue cubriendo la salida, lo hace: menos memoria.</summary>
    private SKBitmap? Decode(SKCodec codec)
    {
        var size = codec.Info.Size;
        var shortSide = Math.Min(size.Width, size.Height);
        var target = Math.Min(shortSide, _options.OutputSize);
        if (shortSide > target)
        {
            var scaled = codec.GetScaledDimensions((float)target / shortSide);
            if (scaled.Width > 0 && scaled.Height > 0 && Math.Min(scaled.Width, scaled.Height) >= target)
                size = scaled;
        }

        var alpha = codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul;
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, alpha, SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap(info);
        // Cualquier cosa distinta de Success (incluido IncompleteInput, que deja la imagen a medias) es corrupta.
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            bitmap.Dispose();
            return null;
        }
        return bitmap;
    }

    /// <summary>Aplica la orientación EXIF (que se pierde al re-codificar). Devuelve null si ya está derecha.</summary>
    private static SKBitmap? ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft)
            return null;

        var swap =
            origin
            is SKEncodedOrigin.LeftTop
                or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom
                or SKEncodedOrigin.LeftBottom;
        var w = swap ? source.Height : source.Width;
        var h = swap ? source.Width : source.Height;

        // x' = scaleX·x + skewX·y + transX ; y' = skewY·x + scaleY·y + transY
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        var target = new SKBitmap(new SKImageInfo(w, h, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return target;
    }

    /// <summary>Recorta el cuadrado central y lo reduce a OutputSize sin agrandar nunca. Para reducciones
    /// grandes primero divide a la mitad (promedio 2×2) y termina con Mitchell: evita el aliasing.</summary>
    private SKBitmap CropAndResize(SKBitmap source)
    {
        var side = Math.Min(source.Width, source.Height);
        var target = Math.Min(side, _options.OutputSize);
        var subset = SKRectI.Create((source.Width - side) / 2, (source.Height - side) / 2, side, side);

        var current = new SKBitmap();
        if (!source.ExtractSubset(current, subset))
        {
            current.Dispose();
            throw new InvalidOperationException("Could not extract the square crop.");
        }

        var halving = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        while (current.Width >= target * 2)
        {
            var half = (current.Width + 1) / 2;
            var next = Resize(current, half, halving);
            current.Dispose();
            current = next;
        }

        if (current.Width != target)
        {
            var next = Resize(current, target, new SKSamplingOptions(SKCubicResampler.Mitchell));
            current.Dispose();
            current = next;
        }
        return current;
    }

    private static SKBitmap Resize(SKBitmap source, int side, SKSamplingOptions sampling) =>
        source.Resize(new SKImageInfo(side, side, source.ColorType, source.AlphaType), sampling)
        ?? throw new InvalidOperationException("Could not resize the image.");

    /// <summary>WebP con pérdida desde píxeles crudos: el encoder no recibe EXIF/XMP/GPS porque los píxeles
    /// no los tienen. Eso también neutraliza archivos políglotas.</summary>
    private byte[] EncodeWebp(SKBitmap bitmap)
    {
        // Sin espacio de color: los píxeles ya son sRGB y así el WebP tampoco lleva chunk ICCP.
        using var pixmap = new SKPixmap(bitmap.Info.WithColorSpace(null), bitmap.GetPixels(), bitmap.RowBytes);
        using var encoded =
            pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, _options.Quality))
            ?? throw new InvalidOperationException("Could not encode the avatar as WebP.");
        return encoded.ToArray();
    }

    /// <summary>Lee el stream completo pero corta en cuanto pasa de <paramref name="maxBytes"/>: un cliente
    /// no puede hacernos reservar más que el tope. Devuelve null si lo excede.</summary>
    internal static async Task<byte[]?> ReadBoundedAsync(Stream input, long maxBytes, CancellationToken ct)
    {
        if (input.CanSeek && input.Length - input.Position > maxBytes)
            return null;

        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(ReadChunkSize);
        try
        {
            int read;
            while ((read = await input.ReadAsync(chunk.AsMemory(0, ReadChunkSize), ct)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                    return null;
                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
        return buffer.ToArray();
    }
}
