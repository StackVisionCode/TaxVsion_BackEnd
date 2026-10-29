using System.Text;
using BuildingBlocks.Imaging;
using BuildingBlocks.Results;
using SkiaSharp;
using Xunit;
using static TaxVision.BuildingBlocks.Tests.Imaging.AvatarTestImages;

namespace TaxVision.BuildingBlocks.Tests.Imaging;

/// <summary>
/// F1 avatares: el normalizador es la única barrera entre lo que sube un usuario y lo que se guarda y
/// se sirve anónimo. Se fija cada guardrail: formato real, bomba de descompresión, animación,
/// orientación EXIF, borrado de metadatos (GPS), recorte al centro, tamaño y determinismo.
/// </summary>
public sealed class AvatarImageNormalizerTests
{
    private const int SizeBudgetBytes = 100 * 1024;

    private static readonly HashSet<string> AllowedWebpChunks = ["VP8 ", "VP8L", "VP8X", "ALPH"];

    private readonly AvatarImageNormalizer _normalizer = new();

    // ------------------------------------------------------------------ formatos aceptados

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void FormatoAceptado_SaleWebpDe512SinMetadatosYDentroDelPresupuesto(SKEncodedImageFormat format)
    {
        using var source = PhotoLike(1600, 1200);

        var avatar = AssertSuccess(_normalizer.Normalize(Encode(source, format)));

        Assert.Equal("image/webp", avatar.ContentType);
        Assert.Equal(512, avatar.Width);
        Assert.Equal(512, avatar.Height);
        Assert.True(avatar.Content.Length < SizeBudgetBytes, $"Output is {avatar.Content.Length} bytes.");
        AssertCleanWebp(avatar.Content, 512);
    }

    [Fact]
    public void PngConTransparencia_SeAceptaYConservaElAlfa()
    {
        using var source = Solid(300, 300, new SKColor(20, 200, 20, 128));

        var avatar = AssertSuccess(_normalizer.Normalize(Encode(source, SKEncodedImageFormat.Png)));

        AssertCleanWebp(avatar.Content, 300);
        using var decoded = Decode(avatar.Content);
        Assert.InRange(decoded.GetPixel(150, 150).Alpha, 100, 160);
    }

    [Fact]
    public async Task NormalizeAsync_LeeElStreamYDaLoMismoQueNormalize()
    {
        using var source = PhotoLike(800, 800);
        var bytes = Encode(source, SKEncodedImageFormat.Jpeg);

        var fromStream = AssertSuccess(await _normalizer.NormalizeAsync(new NonSeekableStream(bytes)));
        var fromBytes = AssertSuccess(_normalizer.Normalize(bytes));

        Assert.Equal(fromBytes.Sha256, fromStream.Sha256);
    }

    // ------------------------------------------------------------------ recorte y tamaño

    [Theory]
    [InlineData(1200, 600)]
    [InlineData(600, 1200)]
    public void ImagenNoCuadrada_SeRecortaAlCentro(int width, int height)
    {
        using var source = CenterTarget(width, height);

        var avatar = AssertSuccess(_normalizer.Normalize(Encode(source, SKEncodedImageFormat.Png)));

        Assert.Equal(avatar.Width, avatar.Height);
        using var decoded = Decode(avatar.Content);
        // Las esquinas del cuadrado central son verdes (el rojo de los lados quedó fuera)…
        foreach (var (x, y) in new[] { (8, 8), (503, 8), (8, 503), (503, 503) })
            AssertColor(Green, decoded.GetPixel(x, y));
        // …y el cuadro azul del centro sobrevive.
        AssertColor(Blue, decoded.GetPixel(256, 256));
    }

    [Fact]
    public void ImagenChica_NoSeAgranda()
    {
        using var source = PhotoLike(100, 80);

        var avatar = AssertSuccess(_normalizer.Normalize(Encode(source, SKEncodedImageFormat.Png)));

        Assert.Equal(80, avatar.Width);
        Assert.Equal(80, avatar.Height);
        AssertCleanWebp(avatar.Content, 80);
    }

    [Fact]
    public void LadoCortoEnElMinimo_SeAcepta()
    {
        using var source = PhotoLike(64, 64);

        var avatar = AssertSuccess(_normalizer.Normalize(Encode(source, SKEncodedImageFormat.Png)));

        Assert.Equal(64, avatar.Width);
    }

    [Fact]
    public void LadoCortoBajoElMinimo_SeRechaza()
    {
        using var source = PhotoLike(400, 63);

        AssertFailure(AvatarImageErrors.TooSmall, _normalizer.Normalize(Encode(source, SKEncodedImageFormat.Png)));
    }

    // ------------------------------------------------------------------ EXIF: orientación y GPS

    [Theory]
    [InlineData((ushort)1, "red")] // sin rotación: arriba-izquierda sigue rojo
    [InlineData((ushort)6, "blue")] // 90° horario: arriba-izquierda pasa a ser el abajo-izquierda azul
    [InlineData((ushort)8, "green")] // 90° antihorario: arriba-izquierda pasa a ser el arriba-derecha verde
    [InlineData((ushort)3, "yellow")] // 180°
    public void OrientacionExif_SeAplicaAntesDeBorrarLosMetadatos(ushort orientation, string expectedTopLeft)
    {
        using var source = Quadrants(300);
        var jpeg = WithExif(Encode(source, SKEncodedImageFormat.Jpeg, 95), orientation);

        var avatar = AssertSuccess(_normalizer.Normalize(jpeg));

        using var decoded = Decode(avatar.Content);
        var expected = expectedTopLeft switch
        {
            "red" => Red,
            "blue" => Blue,
            "green" => Green,
            _ => Yellow,
        };
        AssertColor(expected, decoded.GetPixel(75, 75));
    }

    [Fact]
    public void ExifConGps_NoSobreviveEnLaSalida()
    {
        using var source = Quadrants(600);
        var jpeg = WithExif(Encode(source, SKEncodedImageFormat.Jpeg, 95), orientation: 6);

        // Control: la fixture sí trae EXIF, GPS y el marcador, y Skia lee su orientación.
        Assert.True(Contains(jpeg, "Exif\0\0"u8.ToArray()));
        Assert.True(Contains(jpeg, Encoding.ASCII.GetBytes(ExifSecret)));
        Assert.True(Contains(jpeg, GpsLatitudeSeconds));
        using (var inputCodec = SKCodec.Create(new SKMemoryStream(jpeg)))
            Assert.Equal(SKEncodedOrigin.RightTop, inputCodec.EncodedOrigin);

        var avatar = AssertSuccess(_normalizer.Normalize(jpeg));

        // Firmas exactas (no case-insensitive de 3 letras, que podría aparecer por azar en datos comprimidos).
        Assert.False(Contains(avatar.Content, "Exif"u8.ToArray()));
        Assert.False(Contains(avatar.Content, "EXIF"u8.ToArray()));
        Assert.False(Contains(avatar.Content, "XMP "u8.ToArray()));
        Assert.False(Contains(avatar.Content, Encoding.ASCII.GetBytes(ExifSecret)));
        Assert.False(Contains(avatar.Content, GpsLatitudeSeconds));
        AssertCleanWebp(avatar.Content, 512);
        using var outputCodec = SKCodec.Create(new SKMemoryStream(avatar.Content));
        Assert.Equal(SKEncodedOrigin.TopLeft, outputCodec.EncodedOrigin);
    }

    // ------------------------------------------------------------------ rechazos

    [Fact]
    public void Gif_SeRechazaPorFormato() =>
        AssertFailure(AvatarImageErrors.UnsupportedFormat, _normalizer.Normalize(Gif1X1));

    [Fact]
    public void Bmp_SeRechazaPorFormato() =>
        AssertFailure(AvatarImageErrors.UnsupportedFormat, _normalizer.Normalize(Bmp(128, 128)));

    [Fact]
    public void Svg_SeRechazaPorFormato() =>
        AssertFailure(AvatarImageErrors.UnsupportedFormat, _normalizer.Normalize(Svg()));

    [Fact]
    public void Heic_SeRechazaPorFormato() =>
        AssertFailure(AvatarImageErrors.UnsupportedFormat, _normalizer.Normalize(HeicHeader()));

    [Fact]
    public void BytesAlAzar_SeRechazanPorFormato()
    {
        var random = new byte[4096];
        new Random(42).NextBytes(random);

        AssertFailure(AvatarImageErrors.UnsupportedFormat, _normalizer.Normalize(random));
    }

    [Fact]
    public void JpegTruncado_EsCorrupto()
    {
        using var source = PhotoLike(800, 800);
        var jpeg = Encode(source, SKEncodedImageFormat.Jpeg);

        AssertFailure(AvatarImageErrors.Corrupt, _normalizer.Normalize(jpeg.AsSpan(0, jpeg.Length / 3)));
    }

    [Fact]
    public void PngTruncado_EsCorrupto()
    {
        using var source = PhotoLike(400, 400);
        var png = Encode(source, SKEncodedImageFormat.Png);

        AssertFailure(AvatarImageErrors.Corrupt, _normalizer.Normalize(png.AsSpan(0, png.Length / 2)));
    }

    [Fact]
    public void Vacio_SeRechaza() => AssertFailure(AvatarImageErrors.Empty, _normalizer.Normalize([]));

    [Fact]
    public void MasDe5Mb_SeRechazaSinParsear()
    {
        var bytes = new byte[AvatarImageOptions.DefaultMaxInputBytes + 1];

        AssertFailure(AvatarImageErrors.TooLarge, _normalizer.Normalize(bytes));
    }

    [Fact]
    public async Task MasDe5MbPorStream_SeCortaAlPasarElTope()
    {
        var bytes = new byte[AvatarImageOptions.DefaultMaxInputBytes + 1];

        AssertFailure(AvatarImageErrors.TooLarge, await _normalizer.NormalizeAsync(new NonSeekableStream(bytes)));
        AssertFailure(AvatarImageErrors.TooLarge, await _normalizer.NormalizeAsync(new MemoryStream(bytes)));
    }

    [Fact]
    public async Task Exactamente5MbPorStream_PasaElTopeDeBytes()
    {
        // 5 MB exactos no son "demasiado grandes": fallan después, por formato.
        var bytes = new byte[AvatarImageOptions.DefaultMaxInputBytes];

        AssertFailure(
            AvatarImageErrors.UnsupportedFormat,
            await _normalizer.NormalizeAsync(new NonSeekableStream(bytes))
        );
    }

    [Theory]
    [InlineData(10_000, 10_000)] // 100 MP, cada lado bajo el máximo
    [InlineData(13_000, 100)] // lado por encima de 12000
    [InlineData(65_535, 65_535)]
    public void EncabezadoConDimensionesEnormes_SeRechazaAntesDeDecodificar(int width, int height)
    {
        var png = PngHeaderClaiming(width, height);
        Assert.True(png.Length < 100); // la "bomba" pesa bytes

        AssertFailure(AvatarImageErrors.TooManyPixels, _normalizer.Normalize(png));
    }

    [Fact]
    public void WebpAnimado_SeRechaza()
    {
        var webp = AnimatedWebp(side: 128, frames: 2);
        // Control: el codec de Skia ve los dos frames por sí solo (además del bit VP8X).
        using (var codec = SKCodec.Create(new SKMemoryStream(webp)))
        {
            Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
            Assert.Equal(2, codec.FrameCount);
        }

        AssertFailure(AvatarImageErrors.Animated, _normalizer.Normalize(webp));
    }

    [Fact]
    public void PngAnimado_SeRechaza()
    {
        using var source = Solid(128, 128, Green);
        var apng = WithApngControl(Encode(source, SKEncodedImageFormat.Png));

        AssertFailure(AvatarImageErrors.Animated, _normalizer.Normalize(apng));
    }

    // ------------------------------------------------------------------ determinismo y opciones

    [Fact]
    public void MismaEntrada_MismoSha256()
    {
        using var source = PhotoLike(1024, 768);
        var jpeg = WithExif(Encode(source, SKEncodedImageFormat.Jpeg), orientation: 6);

        var first = AssertSuccess(_normalizer.Normalize(jpeg));
        var second = AssertSuccess(new AvatarImageNormalizer().Normalize(jpeg));

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(first.Content, second.Content);
        Assert.Matches("^[0-9a-f]{64}$", first.Sha256);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(first.Content)),
            first.Sha256
        );
    }

    [Fact]
    public void OpcionesPropias_CambianLosLimites()
    {
        var normalizer = new AvatarImageNormalizer(
            new AvatarImageOptions { OutputSize = 256, MaxInputBytes = 8 * 1024 }
        );
        using var big = PhotoLike(600, 600);
        using var small = Solid(300, 300, Green);
        var smallPng = Encode(small, SKEncodedImageFormat.Png);
        Assert.True(smallPng.Length <= 8 * 1024, $"Fixture is {smallPng.Length} bytes.");

        AssertFailure(AvatarImageErrors.TooLarge, normalizer.Normalize(Encode(big, SKEncodedImageFormat.Png)));
        Assert.Equal(256, AssertSuccess(normalizer.Normalize(smallPng)).Width);
    }

    [Theory]
    [InlineData(0, 512, 64)]
    [InlineData(80, 512, 600)]
    [InlineData(80, 0, 64)]
    [InlineData(101, 512, 64)]
    public void OpcionesInvalidas_FallanAlConstruir(int quality, int outputSize, int minSide) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AvatarImageNormalizer(
                new AvatarImageOptions
                {
                    Quality = quality,
                    OutputSize = outputSize,
                    MinSide = minSide,
                }
            )
        );

    // ------------------------------------------------------------------ helpers

    private static NormalizedAvatar AssertSuccess(Result<NormalizedAvatar> result)
    {
        Assert.True(result.IsSuccess, $"Expected success but got {result.Error.Code}: {result.Error.Message}");
        return result.Value;
    }

    private static void AssertFailure(Error expected, Result<NormalizedAvatar> result)
    {
        Assert.True(result.IsFailure, "Expected failure but normalization succeeded.");
        Assert.Equal(expected.Code, result.Error.Code);
    }

    /// <summary>WebP real, cuadrado, de un frame, y solo con chunks de imagen (sin EXIF/XMP/ICCP).</summary>
    private static void AssertCleanWebp(byte[] content, int side)
    {
        Assert.Equal("RIFF"u8.ToArray(), content[..4]);
        Assert.Equal("WEBP"u8.ToArray(), content[8..12]);
        Assert.All(RiffChunkTypes(content), type => Assert.Contains(type, AllowedWebpChunks));

        using var codec = SKCodec.Create(new SKMemoryStream(content));
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Webp, codec.EncodedFormat);
        Assert.True(codec.FrameCount <= 1, $"FrameCount = {codec.FrameCount}"); // Skia reporta 0 en WebP fijo
        Assert.Equal(side, codec.Info.Width);
        Assert.Equal(side, codec.Info.Height);
    }

    /// <summary>Compara con tolerancia: JPEG y WebP con pérdida corren un poco los colores.</summary>
    private static void AssertColor(SKColor expected, SKColor actual)
    {
        const int tolerance = 40;
        Assert.True(
            Math.Abs(expected.Red - actual.Red) <= tolerance
                && Math.Abs(expected.Green - actual.Green) <= tolerance
                && Math.Abs(expected.Blue - actual.Blue) <= tolerance,
            $"Expected ~{expected} but got {actual}."
        );
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    /// <summary>Stream sin Length (como el body de una petición chunked).</summary>
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
