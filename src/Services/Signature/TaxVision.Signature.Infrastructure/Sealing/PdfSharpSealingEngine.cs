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
            case SignatureFieldKind.Initials:
                // Las iniciales se derivan del nombre del firmante (no se estampa el nombre completo ni
                // la imagen de la firma). Ej. "Amanda B Martinez" → "ABM".
                DrawInitialsStamp(
                    gfx,
                    ToInitials(field.SignerDisplayName),
                    contentX,
                    rect.Y,
                    contentWidth,
                    rect.Height,
                    textPrimary
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
                DrawTextValueStamp(gfx, field.Value, contentX, rect.Y, contentWidth, rect.Height, textPrimary);
                break;
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
        // Layout en bandas apiladas PROPORCIONALES al alto (estilo DocuSign / Adobe Sign), no con offsets
        // fijos: así, aunque el preparador achique mucho la caja del campo, la firma, el caption y la fecha
        // nunca se solapan. La firma es la banda dominante; el caption se omite si no cabe sin comérsela, y
        // la fecha vive en una franja inferior propia separada por una línea.
        var mutedBrush = new XSolidBrush(textMuted);
        var primaryBrush = new XSolidBrush(textPrimary);

        // Caja chica → se prioriza la firma. Bajo ~34pt de alto no cabe el caption sin aplastar la firma.
        var showCaption = height >= 34;
        var captionBand = showCaption ? Math.Min(10.0, height * 0.20) : 0.0;
        var metaBand = Math.Min(11.0, height * 0.24); // franja inferior para la fecha
        var gap = Math.Min(2.0, height * 0.04);
        var signatureBand = Math.Max(1.0, height - captionBand - metaBand - gap);

        if (showCaption)
        {
            var captionFont = new XFont("Helvetica", Math.Clamp(captionBand * 0.62, 4.5, 6.5), XFontStyleEx.Bold);
            gfx.DrawString(
                "DIGITALLY SIGNED BY",
                captionFont,
                mutedBrush,
                new XRect(x, y, width, captionBand),
                XStringFormats.CenterLeft
            );
        }

        // Banda de la firma: la imagen capturada (dibujada/subida/tipografiada) o, si no hay, el nombre en
        // cursiva como fallback tipográfico. Ocupa el grueso de la caja.
        var signatureArea = new XRect(x, y + captionBand, width, signatureBand);
        if (field.SignatureImageBytes is { Length: > 0 } imageBytes)
        {
            DrawSignatureImageFitted(gfx, imageBytes, signatureArea);
        }
        else
        {
            var scriptFont = new XFont(
                "Times New Roman",
                Math.Clamp(signatureBand * 0.72, 9, 24),
                XFontStyleEx.BoldItalic
            );
            gfx.DrawString(field.SignerDisplayName, scriptFont, primaryBrush, signatureArea, XStringFormats.CenterLeft);
        }

        // Franja inferior: línea fina + fecha UTC, en su propia banda (nunca encima de la firma).
        var metaTop = y + height - metaBand;
        gfx.DrawLine(new XPen(XColor.FromArgb(180, 190, 205), 0.4), x, metaTop, x + width, metaTop);
        var metaFont = new XFont("Helvetica", Math.Clamp(metaBand * 0.52, 4.5, 6.5), XFontStyleEx.Regular);
        gfx.DrawString(
            $"{field.SignedAtUtc:yyyy-MM-dd HH:mm 'UTC'}",
            metaFont,
            mutedBrush,
            new XRect(x, metaTop, width, metaBand),
            XStringFormats.CenterLeft
        );
    }

    /// <summary>
    /// Estampa el PNG de la firma dentro de <paramref name="area"/> preservando el aspecto (letterbox),
    /// alineado a la izquierda y centrado verticalmente. Si la imagen no se puede decodificar, se ignora
    /// en silencio: el sellado no debe abortar por una firma malformada (la validación real vive en la subida).
    /// </summary>
    private static void DrawSignatureImageFitted(XGraphics gfx, byte[] imageBytes, XRect area)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes, writable: false);
            using var image = XImage.FromStream(stream);

            if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
                return;

            var aspect = (double)image.PixelWidth / image.PixelHeight;
            var targetWidth = area.Width;
            var targetHeight = targetWidth / aspect;
            if (targetHeight > area.Height)
            {
                targetHeight = area.Height;
                targetWidth = targetHeight * aspect;
            }

            var drawX = area.X;
            var drawY = area.Y + (area.Height - targetHeight) / 2;
            gfx.DrawImage(image, drawX, drawY, targetWidth, targetHeight);
        }
        catch (Exception)
        {
            // Imagen ilegible/corrupta: dejamos el campo sin estampa gráfica en lugar de romper el sellado.
        }
    }

    /// <summary>Iniciales del firmante a partir de su nombre: primera letra de cada palabra, máx 4, mayúsculas.</summary>
    private static string ToInitials(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "—";
        var chars = parts.Where(p => char.IsLetter(p[0])).Select(p => char.ToUpperInvariant(p[0])).Take(4);
        var initials = new string(chars.ToArray());
        return initials.Length == 0 ? "—" : initials;
    }

    /// <summary>Estampa las iniciales centradas en cursiva (sin caption ni imagen), estilo firma manuscrita.</summary>
    private static void DrawInitialsStamp(
        XGraphics gfx,
        string initials,
        double x,
        double y,
        double width,
        double height,
        XColor textPrimary
    )
    {
        var font = new XFont("Times New Roman", Math.Max(11, height * 0.5), XFontStyleEx.BoldItalic);
        var area = new XRect(x, y, width, height);
        gfx.DrawString(initials, font, new XSolidBrush(textPrimary), area, XStringFormats.Center);
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

    /// <summary>
    /// Estampa un campo de texto libre (P4): SÓLO el TEXTO que escribió el firmante, con salto de
    /// línea automático por ancho y centrado verticalmente. La etiqueta/instrucción del preparador
    /// (p. ej. "Put your age") es una guía para el firmante en la pantalla de firma, NO se sella en
    /// el documento: en el PDF final debe aparecer únicamente el valor. El texto se recorta si excede
    /// el alto; un valor vacío no dibuja nada.
    /// </summary>
    private static void DrawTextValueStamp(
        XGraphics gfx,
        string? value,
        double x,
        double y,
        double width,
        double height,
        XColor textPrimary
    )
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var valueFont = new XFont("Helvetica", 9, XFontStyleEx.Regular);
        var lineHeight = valueFont.GetHeight();
        var brush = new XSolidBrush(textPrimary);

        var lines = WrapText(gfx, value, valueFont, width);
        var maxY = y + height - 2;

        // Centrado vertical del bloque de texto dentro del alto disponible.
        var blockHeight = lines.Count * lineHeight;
        var textY = y + Math.Max(0, (height - blockHeight) / 2) + lineHeight;
        foreach (var line in lines)
        {
            if (textY > maxY)
                break;
            gfx.DrawString(line, valueFont, brush, new XPoint(x, textY));
            textY += lineHeight;
        }
    }

    /// <summary>Parte un texto en líneas que caben en <paramref name="maxWidth"/>, respetando saltos de línea.</summary>
    private static IReadOnlyList<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var current = words[0];
            for (var i = 1; i < words.Length; i++)
            {
                var candidate = $"{current} {words[i]}";
                if (gfx.MeasureString(candidate, font).Width <= maxWidth)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = words[i];
                }
            }
            lines.Add(current);
        }
        return lines;
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
            gfx.DrawString("TaxProffice", labelFont, accentBrush, new XPoint(24, y + 2));
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
