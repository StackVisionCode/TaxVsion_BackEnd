using System.Security.Cryptography;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;

// ICmsPdfSigner puede no estar registrado en dev — se acepta null como "sin firma CMS".

namespace TaxVision.Signature.Infrastructure.Sealing;

/// <summary>
/// Implementación por defecto del <see cref="IDocumentSealingEngine"/> basada en
/// <c>PdfSharp</c>. Abre el PDF original en modo <c>Modify</c>, dibuja un recuadro
/// con nombre + timestamp por cada campo, agrega un pie de página con el trailer
/// audit y devuelve los bytes serializados.
///
/// <para>Consideraciones:</para>
/// <list type="bullet">
///   <item>Coordenadas de <see cref="SealedFieldRender"/> están en [0..1] respecto al
///     tamaño de la página; PdfSharp usa puntos con origen top-left en <c>XGraphics</c>
///     luego de <c>PdfPageDefaultXGraphicsMode</c>, así que multiplicamos por
///     <c>page.Width</c>/<c>Height</c>.</item>
///   <item>Sin dependencia de GDI: PdfSharp 6.x compila puro managed en .NET 10.</item>
///   <item>El hash se calcula sobre el buffer final entregado al caller.</item>
/// </list>
/// </summary>
public sealed class PdfSharpSealingEngine(ICmsPdfSigner? cmsSigner = null) : IDocumentSealingEngine
{
    public SealingResult Seal(SealingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var input = new MemoryStream(request.OriginalPdfBytes, writable: false);
        using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        StampAllFields(pdf, request.Fields);
        AppendAuditFooter(pdf, request);

        using var output = new MemoryStream();
        pdf.Save(output, closeStream: false);
        var visuallySealed = output.ToArray();

        // Aplica firma CMS/PKCS#7 (base de PAdES-B) si el signer está configurado.
        var finalBytes = cmsSigner is null ? visuallySealed : cmsSigner.Sign(visuallySealed).SignedPdfBytes;
        return new SealingResult(finalBytes, ComputeSha256(finalBytes));
    }

    // ------------------------------------------------------------------
    // Métodos privados: una responsabilidad por método
    // ------------------------------------------------------------------

    private static void StampAllFields(PdfDocument pdf, IReadOnlyList<SealedFieldRender> fields)
    {
        foreach (var field in fields)
        {
            var pageIndex = field.Page - 1;
            if (pageIndex < 0 || pageIndex >= pdf.PageCount)
                continue;

            var page = pdf.Pages[pageIndex];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            DrawFieldBox(gfx, page, field);
        }
    }

    private static void DrawFieldBox(XGraphics gfx, PdfPage page, SealedFieldRender field)
    {
        var pageWidth = page.Width.Point;
        var pageHeight = page.Height.Point;

        var rect = new XRect(
            field.X * pageWidth,
            field.Y * pageHeight,
            field.Width * pageWidth,
            field.Height * pageHeight
        );

        var pen = new XPen(XColors.Navy, 0.75);
        var background = new XSolidBrush(XColor.FromArgb(32, 0, 32, 96));
        gfx.DrawRectangle(pen, background, rect);

        var textBrush = new XSolidBrush(XColors.Navy);
        var titleFont = new XFont("Helvetica", 8, XFontStyleEx.Bold);
        var subtitleFont = new XFont("Helvetica", 7, XFontStyleEx.Regular);

        var title = BuildFieldTitle(field);
        var subtitle = $"{field.SignerDisplayName}  •  {field.SignedAtUtc:yyyy-MM-dd HH:mm 'UTC'}";

        gfx.DrawString(title, titleFont, textBrush, new XPoint(rect.X + 2, rect.Y + 10));
        gfx.DrawString(subtitle, subtitleFont, textBrush, new XPoint(rect.X + 2, rect.Y + 20));
    }

    private static string BuildFieldTitle(SealedFieldRender field) =>
        field.Kind switch
        {
            SignatureFieldKind.Signature => "SIGNED",
            SignatureFieldKind.Initials => "INITIALS",
            SignatureFieldKind.Date => "DATE",
            SignatureFieldKind.Text => field.Label ?? "TEXT",
            SignatureFieldKind.Checkbox => "CHECK",
            _ => field.Label ?? string.Empty,
        };

    private static void AppendAuditFooter(PdfDocument pdf, SealingRequest request)
    {
        var footerFont = new XFont("Helvetica", 7, XFontStyleEx.Regular);
        var footerBrush = new XSolidBrush(XColors.DimGray);
        var hashLine = $"Original SHA-256: {request.DocumentHashPre}";

        for (var i = 0; i < pdf.PageCount; i++)
        {
            var page = pdf.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var y = page.Height.Point - 18;
            gfx.DrawString(request.AuditFooter, footerFont, footerBrush, new XPoint(24, y));
            gfx.DrawString(hashLine, footerFont, footerBrush, new XPoint(24, y + 9));
        }
    }

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
