using TaxVision.Signature.Application.Requests.Public;

namespace TaxVision.Signature.Tests.Requests.Public;

/// <summary>
/// El endpoint anónimo de subida de firma sólo debe aceptar PNG acotados: se valida por bytes
/// (magic + IHDR + dimensiones) antes de tocar MinIO, sin decodificar la imagen.
/// </summary>
public sealed class SignatureImageValidatorTests
{
    // 8 bytes de firma PNG + longitud IHDR + "IHDR" + ancho + alto (24 bytes mínimos inspeccionables).
    private static byte[] BuildPng(uint width, uint height)
    {
        var bytes = new byte[24];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes, 0);
        // length = 13 (IHDR data length), no lo lee el validador pero lo dejamos correcto.
        bytes[8] = 0x00;
        bytes[9] = 0x00;
        bytes[10] = 0x00;
        bytes[11] = 0x0D;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static void WriteBigEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    [Fact]
    public void Valid_png_passes()
    {
        var result = SignatureImageValidator.Validate(BuildPng(600, 200));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Null_is_rejected()
    {
        var result = SignatureImageValidator.Validate(null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Image.Empty", result.Error.Code);
    }

    [Fact]
    public void Empty_is_rejected()
    {
        var result = SignatureImageValidator.Validate([]);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Image.Empty", result.Error.Code);
    }

    [Fact]
    public void Oversized_payload_is_rejected()
    {
        var oversized = new byte[SignatureImageValidator.MaxBytes + 1];

        var result = SignatureImageValidator.Validate(oversized);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Image.TooLarge", result.Error.Code);
    }

    [Fact]
    public void Non_png_magic_is_rejected()
    {
        // Cabecera JPEG (FF D8 FF) rellenada al mínimo inspeccionable.
        var jpeg = new byte[24];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        jpeg[2] = 0xFF;

        var result = SignatureImageValidator.Validate(jpeg);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Image.NotPng", result.Error.Code);
    }

    [Fact]
    public void Png_signature_without_ihdr_is_rejected()
    {
        var bytes = BuildPng(600, 200);
        // Corrompe el chunk IHDR.
        bytes[12] = (byte)'X';

        var result = SignatureImageValidator.Validate(bytes);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Image.NotPng", result.Error.Code);
    }

    [Fact]
    public void Zero_dimension_is_rejected()
    {
        var result = SignatureImageValidator.Validate(BuildPng(0, 200));

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Image.BadDimensions", result.Error.Code);
    }

    [Fact]
    public void Oversized_dimension_is_rejected()
    {
        var result = SignatureImageValidator.Validate(BuildPng(5000, 200));

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Image.BadDimensions", result.Error.Code);
    }
}
