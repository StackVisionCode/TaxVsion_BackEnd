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

        // Professional signature stamp inspired by DocuSign / Adobe Sign:
        // no saturated fill, subtle border, italic cursive-style name imitating a manuscript
        // signature, small caption below with "Digitally signed by" and the UTC timestamp.
        var borderColor = XColor.FromArgb(180, 190, 205);
        var accentColor = XColor.FromArgb(20, 45, 90);
        var textPrimary = XColor.FromArgb(24, 30, 42);
        var textMuted = XColor.FromArgb(96, 105, 120);

        var borderPen = new XPen(borderColor, 0.4);
        gfx.DrawRoundedRectangle(borderPen, XBrushes.White, rect, new XSize(3, 3));

        // Thin left accent stripe (2pt wide).
        var accentRect = new XRect(rect.X, rect.Y, 2, rect.Height);
        gfx.DrawRectangle(new XSolidBrush(accentColor), accentRect);

        var contentX = rect.X + 6;
        var contentWidth = Math.Max(0, rect.Width - 8);

        switch (field.Kind)
        {
            case SignatureFieldKind.Signature:
            case SignatureFieldKind.Initials:
                DrawSignatureFieldStamp(
                    gfx,
                    field,
                    contentX,
                    rect.Y,
                    contentWidth,
                    rect.Height,
                    textPrimary,
                    textMuted
                );
                break;
            case SignatureFieldKind.Date:
                DrawSingleValueStamp(
                    gfx,
                    "DATE",
                    field.SignedAtUtc.ToString("yyyy-MM-dd"),
                    contentX,
                    rect.Y,
                    contentWidth,
                    rect.Height,
                    textPrimary,
                    textMuted
                );
                break;
            case SignatureFieldKind.Checkbox:
                DrawSingleValueStamp(
                    gfx,
                    field.Label ?? "CONFIRMED",
                    "✓",
                    contentX,
                    rect.Y,
                    contentWidth,
                    rect.Height,
                    textPrimary,
                    textMuted
                );
                break;
            case SignatureFieldKind.Text:
            default:
                DrawSingleValueStamp(
                    gfx,
                    field.Label ?? "SIGNED",
                    field.SignerDisplayName,
                    contentX,
                    rect.Y,
                    contentWidth,
                    rect.Height,
                    textPrimary,
                    textMuted
                );
                break;
        }
    }

    private static void DrawSignatureFieldStamp(
        XGraphics gfx,
        SealedFieldRender field,
        double x,
        double y,
        double width,
        double height,
        XColor textPrimary,
        XColor textMuted
    )
    {
        var captionFont = new XFont("Helvetica", 5.5, XFontStyleEx.Bold);
        var scriptFont = new XFont("Times New Roman", Math.Max(9, height * 0.42), XFontStyleEx.BoldItalic);
        var metaFont = new XFont("Helvetica", 5.5, XFontStyleEx.Regular);
        var mutedBrush = new XSolidBrush(textMuted);
        var primaryBrush = new XSolidBrush(textPrimary);

        // Top caption.
        gfx.DrawString("DIGITALLY SIGNED BY", captionFont, mutedBrush, new XPoint(x, y + 8));

        // Signer name in italic script style, vertically centered in the middle band.
        var nameArea = new XRect(x, y + 10, width, height * 0.55);
        gfx.DrawString(field.SignerDisplayName, scriptFont, primaryBrush, nameArea, XStringFormats.CenterLeft);

        // Thin underline under the "signature".
        var underlineY = y + height * 0.72;
        gfx.DrawLine(new XPen(XColor.FromArgb(180, 190, 205), 0.4), x, underlineY, x + width, underlineY);

        // Bottom meta line.
        gfx.DrawString(
            $"{field.SignedAtUtc:yyyy-MM-dd HH:mm 'UTC'}",
            metaFont,
            mutedBrush,
            new XPoint(x, y + height - 3)
        );
    }

    private static void DrawSingleValueStamp(
        XGraphics gfx,
        string caption,
        string value,
        double x,
        double y,
        double width,
        double height,
        XColor textPrimary,
        XColor textMuted
    )
    {
        var captionFont = new XFont("Helvetica", 5.5, XFontStyleEx.Bold);
        var valueFont = new XFont("Helvetica", Math.Max(8, height * 0.36), XFontStyleEx.Bold);
        gfx.DrawString(caption.ToUpperInvariant(), captionFont, new XSolidBrush(textMuted), new XPoint(x, y + 8));
        var valueArea = new XRect(x, y + 10, width, height - 12);
        gfx.DrawString(value, valueFont, new XSolidBrush(textPrimary), valueArea, XStringFormats.CenterLeft);
    }

    private static void AppendAuditFooter(PdfDocument pdf, SealingRequest request)
    {
        var monoFont = new XFont("Courier", 6, XFontStyleEx.Regular);
        var labelFont = new XFont("Helvetica", 6, XFontStyleEx.Bold);
        var textFont = new XFont("Helvetica", 6, XFontStyleEx.Regular);
        var mutedBrush = new XSolidBrush(XColor.FromArgb(96, 105, 120));
        var accentBrush = new XSolidBrush(XColor.FromArgb(20, 45, 90));
        var rulePen = new XPen(XColor.FromArgb(210, 215, 225), 0.4);

        var hashChunked = FormatHashChunked(request.DocumentHashPre);

        for (var i = 0; i < pdf.PageCount; i++)
        {
            var page = pdf.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var pageWidth = page.Width.Point;
            var pageHeight = page.Height.Point;
            var y = pageHeight - 22;

            // Thin separator rule.
            gfx.DrawLine(rulePen, 24, y - 4, pageWidth - 24, y - 4);

            // Left: brand + audit line.
            gfx.DrawString("TaxProCore", labelFont, accentBrush, new XPoint(24, y + 2));
            gfx.DrawString($" • {request.AuditFooter}", textFont, mutedBrush, new XPoint(24 + 42, y + 2));

            // Right: page number.
            var pageLabel = $"Page {i + 1} / {pdf.PageCount}";
            var pageSize = gfx.MeasureString(pageLabel, textFont);
            gfx.DrawString(pageLabel, textFont, mutedBrush, new XPoint(pageWidth - 24 - pageSize.Width, y + 2));

            // Second line: original document hash, chunked mono (integrity evidence).
            var refLine = $"Doc SHA-256  {hashChunked}";
            gfx.DrawString(refLine, monoFont, mutedBrush, new XPoint(24, y + 11));
        }
    }

    private static string FormatHashChunked(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return "—";
        var normalized = hex.Replace(" ", string.Empty).Replace("-", string.Empty);
        var chunks = new List<string>(normalized.Length / 8 + 1);
        for (var i = 0; i < normalized.Length; i += 8)
            chunks.Add(normalized.Substring(i, Math.Min(8, normalized.Length - i)));
        return string.Join(" ", chunks);
    }

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
