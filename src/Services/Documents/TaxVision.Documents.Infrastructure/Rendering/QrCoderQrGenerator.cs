using QRCoder;
using TaxVision.Documents.Application.Abstractions;

namespace TaxVision.Documents.Infrastructure.Rendering;

/// <summary>
/// QR con QRCoder. Usa <see cref="PngByteQRCode"/> — no depende de System.Drawing, así que corre en el
/// contenedor Linux sin librerías nativas extra. ECC nivel Q (25% de recuperación) para que el QR siga
/// siendo legible aunque la factura se imprima o se escanee en mala calidad.
/// </summary>
public sealed class QrCoderQrGenerator : IQrCodeGenerator
{
    public string CreatePngDataUri(string content, int pixelsPerModule = 6)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(pixelsPerModule);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }
}
