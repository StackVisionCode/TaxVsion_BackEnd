using System.Globalization;
using System.Security.Cryptography;
using BuildingBlocks.Platform;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

// ICmsPdfSigner puede no estar registrado en dev — se acepta null como "sin firma CMS".

namespace TaxVision.Signature.Infrastructure.Sealing;

/// <summary>
/// Implementación por defecto del <see cref="IDocumentSealingEngine"/> basada en
/// <c>PdfSharp</c>. Abre el PDF original en modo <c>Modify</c>, estampa el contenido de cada
/// campo dentro de su caja (ver <see cref="DrawField"/>), agrega un pie de página con el trailer
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
public sealed class PdfSharpSealingEngine(IPlatformSettings platform, ICmsPdfSigner? cmsSigner = null)
    : IDocumentSealingEngine
{
    public SealingResult Seal(SealingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var input = new MemoryStream(request.OriginalPdfBytes, writable: false);
        using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        StampAllFields(pdf, request.Fields, request.Style);
        AppendAuditFooter(pdf, request, platform.Current.BrandName);

        using var output = new MemoryStream();
        pdf.Save(output, closeStream: false);
        var visuallySealed = output.ToArray();

        // Aplica firma CMS/PKCS#7 (base de PAdES-B) si el signer está configurado.
        var finalBytes = cmsSigner is null ? visuallySealed : cmsSigner.Sign(visuallySealed).SignedPdfBytes;
        return new SealingResult(finalBytes, ComputeSha256(finalBytes));
    }

    // null si no hay signer CMS (sellado degradado): el certificado de completion omite el panel de sello.
    public CertificateSealInfo? DescribeSealCertificate() => cmsSigner?.DescribeCertificate();

    // ------------------------------------------------------------------
    // Métodos privados: una responsabilidad por método
    // ------------------------------------------------------------------

    // internal: reusado por el renderer de copias parciales (F7).
    internal static void StampAllFields(PdfDocument pdf, IReadOnlyList<SealedFieldRender> fields, StampStyle style)
    {
        foreach (var field in fields)
        {
            var pageIndex = field.Page - 1;
            if (pageIndex < 0 || pageIndex >= pdf.PageCount)
                continue;

            var page = pdf.Pages[pageIndex];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            DrawField(gfx, page, field, style);
        }
    }

    // Colores del estampado: tinta casi negra para el contenido, gris para el marco de la firma.
    private static readonly XColor InkColor = XColor.FromArgb(24, 30, 42);
    private static readonly XColor FrameTextColor = XColor.FromArgb(96, 105, 120);
    private static readonly XColor FrameBorderColor = XColor.FromArgb(180, 190, 205);
    private static readonly XColor FrameAccentColor = XColor.FromArgb(20, 45, 90);

    /// <summary>
    /// Estampa un campo dentro de la caja que fijó el preparador. Como en la industria (DocuSign, Dropbox Sign,
    /// Adobe), iniciales, fecha, texto y casilla llevan SOLO su contenido; la firma puede llevar un marco si la
    /// oficina lo tiene activo. La evidencia legal es el sello PAdES y el certificado, no estas cajas.
    /// </summary>
    private static void DrawField(XGraphics gfx, PdfPage page, SealedFieldRender field, StampStyle style)
    {
        var rect = new XRect(
            field.X * page.Width.Point,
            field.Y * page.Height.Point,
            field.Width * page.Width.Point,
            field.Height * page.Height.Point
        );

        switch (field.Kind)
        {
            case SignatureFieldKind.Signature:
                if (style.ShowSignatureFrame)
                    DrawFramedSignature(gfx, field, rect);
                else
                    DrawSignatureInk(gfx, field, rect);
                break;
            case SignatureFieldKind.Initials:
                DrawSingleLine(
                    gfx,
                    ToInitials(field.SignerDisplayName),
                    rect,
                    field.FontSizePt,
                    style,
                    InitialsFont,
                    center: true
                );
                break;
            case SignatureFieldKind.Date:
                var date = field.SignedAtUtc.ToString(style.DatePattern, CultureInfo.InvariantCulture);
                DrawSingleLine(gfx, date, rect, field.FontSizePt, style, TextFont, center: false);
                break;
            case SignatureFieldKind.Text:
                DrawWrappedText(gfx, field.Value, rect, field.FontSizePt, style);
                break;
            case SignatureFieldKind.Checkbox:
                DrawCheckMark(gfx, rect);
                break;
            default:
                DrawSingleLine(gfx, field.SignerDisplayName, rect, field.FontSizePt, style, TextFont, center: false);
                break;
        }
    }

    // Tipografías: las iniciales imitan una marca manuscrita; fecha y texto, letra de formulario.
    private static readonly (string Family, XFontStyleEx Style) InitialsFont = (
        "Times New Roman",
        XFontStyleEx.BoldItalic
    );
    private static readonly (string Family, XFontStyleEx Style) TextFont = ("Helvetica", XFontStyleEx.Regular);

    /// <summary>
    /// Una línea (iniciales, fecha) centrada en vertical: arranca en la letra del preparador o en la automática
    /// (<see cref="FieldTypography.StartingFontSize"/>) y solo se achica si no cabe en la caja. Nunca crece.
    /// </summary>
    private static void DrawSingleLine(
        XGraphics gfx,
        string text,
        XRect rect,
        int? fontSizePt,
        StampStyle style,
        (string Family, XFontStyleEx Style) typeface,
        bool center
    )
    {
        if (string.IsNullOrWhiteSpace(text) || rect.Width <= 0 || rect.Height <= 0)
            return;

        var size = FieldTypography.StartingFontSize(fontSizePt, rect.Height, style.AutoFontMinPt, style.AutoFontMaxPt);
        var font = new XFont(typeface.Family, size, typeface.Style);
        while (
            size > FieldTypography.MinFontSizePt
            && (gfx.MeasureString(text, font).Width > rect.Width || font.GetHeight() > rect.Height)
        )
        {
            size -= 0.5;
            font = new XFont(typeface.Family, size, typeface.Style);
        }

        gfx.DrawString(
            text,
            font,
            new XSolidBrush(InkColor),
            rect,
            center ? XStringFormats.Center : XStringFormats.CenterLeft
        );
    }

    /// <summary>
    /// Texto libre del firmante, con salto de línea por ancho. Arranca en la letra del preparador o la automática
    /// y se achica hasta que el bloque quepa en el alto de la caja; lo que no quepa ni en el mínimo se recorta.
    /// La etiqueta del preparador es una guía en pantalla y NO se estampa.
    /// </summary>
    private static void DrawWrappedText(XGraphics gfx, string? value, XRect rect, int? fontSizePt, StampStyle style)
    {
        if (string.IsNullOrWhiteSpace(value) || rect.Width <= 0 || rect.Height <= 0)
            return;

        var size = FieldTypography.StartingFontSize(fontSizePt, rect.Height, style.AutoFontMinPt, style.AutoFontMaxPt);
        var font = new XFont(TextFont.Family, size, TextFont.Style);
        var lines = WrapText(gfx, value, font, rect.Width);
        while (size > FieldTypography.MinFontSizePt && lines.Count * font.GetHeight() > rect.Height)
        {
            size -= 0.5;
            font = new XFont(TextFont.Family, size, TextFont.Style);
            lines = WrapText(gfx, value, font, rect.Width);
        }

        var lineHeight = font.GetHeight();
        var visible = Math.Max(1, Math.Min(lines.Count, (int)Math.Floor(rect.Height / lineHeight)));
        var top = rect.Y + Math.Max(0, (rect.Height - visible * lineHeight) / 2);
        var brush = new XSolidBrush(InkColor);
        for (var i = 0; i < visible; i++)
            gfx.DrawString(
                lines[i],
                font,
                brush,
                new XRect(rect.X, top + i * lineHeight, rect.Width, lineHeight),
                XStringFormats.CenterLeft
            );
    }

    /// <summary>Firma sin marco: la tinta ocupa toda la caja (sin deformarse), alineada a la izquierda.</summary>
    private static void DrawSignatureInk(XGraphics gfx, SealedFieldRender field, XRect area)
    {
        if (field.SignatureImageBytes is { Length: > 0 } imageBytes)
        {
            DrawSignatureImageFitted(gfx, imageBytes, area);
            return;
        }

        // Sin imagen: el nombre en cursiva como respaldo tipográfico, tan grande como permita la caja.
        var size = Math.Max(FieldTypography.MinFontSizePt, area.Height * FieldTypography.AutoHeightRatio);
        var font = new XFont(InitialsFont.Family, size, InitialsFont.Style);
        while (
            size > FieldTypography.MinFontSizePt && gfx.MeasureString(field.SignerDisplayName, font).Width > area.Width
        )
        {
            size -= 0.5;
            font = new XFont(InitialsFont.Family, size, InitialsFont.Style);
        }
        gfx.DrawString(field.SignerDisplayName, font, new XSolidBrush(InkColor), area, XStringFormats.CenterLeft);
    }

    /// <summary>
    /// Firma con marco (opcional por oficina, activado por defecto): borde fino, barra de acento, arriba
    /// "Signed by {nombre}" y abajo la fecha/hora UTC, en bandas finas para que la tinta conserve el grueso de
    /// la caja. Sin datos internos (el id de la solicitud vive en el certificado, no en cada página).
    /// </summary>
    private static void DrawFramedSignature(XGraphics gfx, SealedFieldRender field, XRect rect)
    {
        gfx.DrawRoundedRectangle(new XPen(FrameBorderColor, 0.4), XBrushes.White, rect, new XSize(2, 2));
        const double accentWidth = 1.5;
        gfx.DrawRectangle(new XSolidBrush(FrameAccentColor), new XRect(rect.X, rect.Y, accentWidth, rect.Height));

        var padding = Math.Min(3.0, rect.Height * 0.08);
        var inner = new XRect(
            rect.X + accentWidth + padding,
            rect.Y + padding,
            Math.Max(0, rect.Width - accentWidth - 2 * padding),
            Math.Max(0, rect.Height - 2 * padding)
        );

        // Bandas de texto del marco: ~14 % del alto cada una (4.5–7 pt); la superior se omite en cajas bajas.
        var captionSize = Math.Clamp(inner.Height * 0.14, 4.5, 7);
        var captionFont = new XFont(TextFont.Family, captionSize, XFontStyleEx.Bold);
        var metaFont = new XFont(TextFont.Family, captionSize, XFontStyleEx.Regular);
        var band = captionFont.GetHeight();
        var showCaption = inner.Height >= 4 * band;
        var mutedBrush = new XSolidBrush(FrameTextColor);

        var inkTop = inner.Y;
        if (showCaption)
        {
            gfx.DrawString(
                $"Signed by {field.SignerDisplayName}",
                captionFont,
                mutedBrush,
                new XRect(inner.X, inner.Y, inner.Width, band),
                XStringFormats.CenterLeft
            );
            inkTop += band;
        }

        var metaTop = inner.Bottom - band;
        gfx.DrawString(
            $"{field.SignedAtUtc:yyyy-MM-dd HH:mm} UTC",
            metaFont,
            mutedBrush,
            new XRect(inner.X, metaTop, inner.Width, band),
            XStringFormats.CenterLeft
        );

        var inkArea = new XRect(inner.X, inkTop, inner.Width, Math.Max(1, metaTop - inkTop));
        DrawSignatureInk(gfx, field, inkArea);
    }

    /// <summary>Casilla marcada: un ✓ trazado (no depende de que la fuente tenga el glifo), centrado en la caja.</summary>
    private static void DrawCheckMark(XGraphics gfx, XRect rect)
    {
        var side = Math.Min(rect.Width, rect.Height) * 0.8;
        var x = rect.X + (rect.Width - side) / 2;
        var y = rect.Y + (rect.Height - side) / 2;
        var pen = new XPen(InkColor, Math.Max(0.8, side * 0.12))
        {
            LineCap = XLineCap.Round,
            LineJoin = XLineJoin.Round,
        };
        gfx.DrawLines(
            pen,
            [
                new XPoint(x + side * 0.12, y + side * 0.55),
                new XPoint(x + side * 0.40, y + side * 0.82),
                new XPoint(x + side * 0.88, y + side * 0.18),
            ]
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
            // Aplanar sobre blanco ANTES de que PdfSharp lo dibuje: un PNG con transparencia (p. ej. una
            // firma dibujada) se estampa NEGRO porque PdfSharp ignora el alfa y pinta los píxeles
            // transparentes (RGB 0,0,0) en negro. Aplanar quita el alfa (fondo blanco) → se ve la firma.
            var opaqueBytes = FlattenOntoWhite(imageBytes);
            using var stream = new MemoryStream(opaqueBytes, writable: false);
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

    /// <summary>
    /// Aplana un PNG/imagen sobre fondo BLANCO y lo re-codifica como PNG SIN canal alfa (RGB 24-bit).
    /// Necesario porque PdfSharp pinta el alfa como negro. Si la imagen no se puede decodificar, devuelve
    /// los bytes originales (el estampado nunca aborta por una firma malformada).
    ///
    /// Usa SkiaSharp (Apache 2.0) en lugar de SixLabors.ImageSharp 3.x (EOL con CVEs sin patch gratis;
    /// ver csproj). SkiaSharp.Decode acepta PNG/JPEG/WEBP/GIF; la decodificación de formatos no soportados
    /// o inválidos devuelve null y caemos al byte[] original.
    /// </summary>
    private static byte[] FlattenOntoWhite(byte[] imageBytes)
    {
        try
        {
            using var original = SKBitmap.Decode(imageBytes);
            if (original is null)
                return imageBytes;

            // Surface opaca RGB888x: SkiaSharp garantiza que al componer un bitmap con alfa sobre un
            // canvas limpiado a blanco, los píxeles transparentes salen BLANCOS (no negros como pintaría
            // PdfSharp). SKColorType.Rgb888x descarta el canal alfa en el snapshot.
            var info = new SKImageInfo(original.Width, original.Height, SKColorType.Rgb888x, SKAlphaType.Opaque);
            using var surface = SKSurface.Create(info);
            if (surface is null)
                return imageBytes;
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(original, 0, 0);
            canvas.Flush();

            using var snapshot = surface.Snapshot();
            // PNG quality 100: PNG es lossless, el parámetro solo afecta el nivel de compresión zlib.
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch (Exception)
        {
            return imageBytes;
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

    private static void AppendAuditFooter(PdfDocument pdf, SealingRequest request, string brandName)
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
            var brandWidth = gfx.MeasureString(brandName, labelFont).Width;

            // Left: brand + audit line. La línea de auditoría arranca donde termina la marca (se mide):
            // un ancho fijo dejaba un hueco o pisaba el texto según lo largo del nombre.
            gfx.DrawString(brandName, labelFont, accentBrush, new XPoint(24, y + 2));
            gfx.DrawString($" • {request.AuditFooter}", textFont, mutedBrush, new XPoint(24 + brandWidth, y + 2));

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

    internal static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
