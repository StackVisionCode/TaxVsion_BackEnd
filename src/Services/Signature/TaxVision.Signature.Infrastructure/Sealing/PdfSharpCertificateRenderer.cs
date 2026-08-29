using System.Security.Cryptography;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Sealing;

/// <summary>
/// Certificate of Completion profesional (estilo DocuSign / Adobe Sign): header de marca (logo de la
/// plataforma y, si existe, el de la oficina emisora), resumen del envelope, hashes de integridad
/// SHA-256, audit trail cronológico por firmante (visto → consent → firmado con IP real y user agent)
/// y footer legal. Pagina de verdad: nunca desborda el margen aunque haya muchos firmantes o textos
/// largos. No expone identificadores internos (TenantId): solo la referencia del envelope.
/// </summary>
public sealed class PdfSharpCertificateRenderer : ICertificateOfCompletionRenderer
{
    private const string BrandName = "TaxProffice";
    private const string BrandProduct = "TaxProffice Signature Service";

    private const double MarginLeft = 48;
    private const double MarginRight = 48;
    private const double MarginTop = 54;
    private const double MarginBottom = 54;
    private const double LabelColWidth = 150;
    private const double LogoHeight = 30;

    private const string SansFamily = "Helvetica";
    private const string MonoFamily = "Courier";

    private static readonly XColor BrandPrimary = XColor.FromArgb(30, 70, 107);
    private static readonly XColor BrandAccent = XColor.FromArgb(0, 122, 132);
    private static readonly XColor TextPrimary = XColor.FromArgb(24, 30, 42);
    private static readonly XColor TextMuted = XColor.FromArgb(96, 105, 120);
    private static readonly XColor RuleColor = XColor.FromArgb(210, 215, 225);
    private static readonly XColor CardBorder = XColor.FromArgb(220, 226, 236);

    // Logo de plataforma embebido (recurso del ensamblado). Se usa cuando el modelo no trae uno propio.
    private static readonly byte[]? PlatformLogoBytes = LoadEmbeddedPlatformLogo();

    public CertificateResult Render(CertificateOfCompletionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        using var pdf = new PdfDocument();
        pdf.Info.Title = $"{BrandName} — Certificate of Completion";
        pdf.Info.Author = BrandProduct;
        pdf.Info.Subject = $"Legal audit trail for signature request {model.SignatureRequestId:D}";
        pdf.Info.Keywords = "e-signature; audit trail; ESIGN; eIDAS; PAdES-B";

        XImage? platformLogo = TryLoadImage(model.PlatformLogo ?? PlatformLogoBytes);
        XImage? tenantLogo = TryLoadImage(model.TenantLogo);
        try
        {
            using var ctx = new RenderContext(pdf);

            WriteHeader(ctx, model, platformLogo, tenantLogo);
            WriteSummary(ctx, model);
            WriteIntegrity(ctx, model);
            WriteSigners(ctx, model.Signers);
            WriteLegalFooter(ctx, model);

            using var output = new MemoryStream();
            pdf.Save(output, closeStream: false);
            var bytes = output.ToArray();
            return new CertificateResult(bytes, ComputeSha256(bytes));
        }
        finally
        {
            platformLogo?.Dispose();
            tenantLogo?.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Sections
    // ------------------------------------------------------------------

    private static void WriteHeader(
        RenderContext ctx,
        CertificateOfCompletionModel model,
        XImage? platformLogo,
        XImage? tenantLogo
    )
    {
        var gfx = ctx.Gfx;
        var titleFont = new XFont(SansFamily, 22, XFontStyleEx.Bold);
        var subtitleFont = new XFont(SansFamily, 10, XFontStyleEx.Regular);
        var badgeFont = new XFont(SansFamily, 7, XFontStyleEx.Bold);

        var top = ctx.CursorY;

        // Marca: logos si hay; si no, chip de texto de la plataforma.
        if (platformLogo is not null || tenantLogo is not null)
        {
            var x = MarginLeft;
            x = DrawLogo(gfx, platformLogo, x, top);
            DrawLogo(gfx, tenantLogo, x, top);
        }
        else
        {
            var chipRect = new XRect(MarginLeft, top, 88, 18);
            gfx.DrawRoundedRectangle(new XSolidBrush(BrandPrimary), chipRect, new XSize(6, 6));
            gfx.DrawString(
                BrandName,
                new XFont(SansFamily, 9, XFontStyleEx.Bold),
                XBrushes.White,
                new XRect(chipRect.X, chipRect.Y + 4, chipRect.Width, chipRect.Height),
                XStringFormats.TopCenter
            );
        }

        var badgeText = "LEGAL AUDIT TRAIL  •  ESIGN / eIDAS";
        var badgeSize = gfx.MeasureString(badgeText, badgeFont);
        gfx.DrawString(
            badgeText,
            badgeFont,
            new XSolidBrush(TextMuted),
            new XPoint(ctx.ContentRight - badgeSize.Width, top + 12)
        );

        ctx.CursorY = top + LogoHeight + 16;
        gfx.DrawString(
            "Certificate of Completion",
            titleFont,
            new XSolidBrush(TextPrimary),
            new XPoint(MarginLeft, ctx.CursorY)
        );
        ctx.CursorY += 20;
        gfx.DrawString(
            "This document certifies the events of the electronic signature process below.",
            subtitleFont,
            new XSolidBrush(TextMuted),
            new XPoint(MarginLeft, ctx.CursorY)
        );

        if (!string.IsNullOrWhiteSpace(model.IssuerName))
        {
            ctx.CursorY += 15;
            var issuerLabel = new XFont(SansFamily, 9, XFontStyleEx.Bold);
            gfx.DrawString("Issued by ", issuerLabel, new XSolidBrush(TextMuted), new XPoint(MarginLeft, ctx.CursorY));
            var w = gfx.MeasureString("Issued by ", issuerLabel).Width;
            gfx.DrawString(
                model.IssuerName,
                new XFont(SansFamily, 9, XFontStyleEx.Bold),
                new XSolidBrush(BrandPrimary),
                new XPoint(MarginLeft + w, ctx.CursorY)
            );
        }

        ctx.CursorY += 8;
        gfx.DrawLine(new XPen(BrandPrimary, 1.2), MarginLeft, ctx.CursorY, ctx.ContentRight, ctx.CursorY);
        ctx.CursorY += 16;
    }

    private static void WriteSummary(RenderContext ctx, CertificateOfCompletionModel model)
    {
        WriteSectionHeader(ctx, "Envelope Summary");
        WriteRow(ctx, "Reference", ShortReference(model.SignatureRequestId));
        WriteRow(ctx, "Status", "Completed");
        WriteRow(ctx, "Category", model.Category.ToString());
        WriteRow(ctx, "Created (UTC)", FormatUtc(model.CreatedAtUtc));
        WriteRow(ctx, "Completed (UTC)", FormatUtc(model.CompletedAtUtc));
        ctx.CursorY += 8;
    }

    private static void WriteIntegrity(RenderContext ctx, CertificateOfCompletionModel model)
    {
        WriteSectionHeader(ctx, "Document Integrity  •  SHA-256");
        WriteHashBlock(ctx, "Original document", model.DocumentHashPre);
        WriteHashBlock(ctx, "Sealed document (after signatures)", model.DocumentHashPost);

        var noteFont = new XFont(SansFamily, 7.5, XFontStyleEx.Italic);
        var note =
            "To verify: recompute the SHA-256 of the sealed PDF and compare it with the sealed hash above. "
            + "A match proves the document has not been altered since signing.";
        WriteWrapped(ctx, note, noteFont, TextMuted, MarginLeft, ctx.ContentWidth, lineHeight: 11);
        ctx.CursorY += 8;
    }

    private static void WriteSigners(RenderContext ctx, IReadOnlyList<CertificateSignerEntry> signers)
    {
        WriteSectionHeader(ctx, $"Signers  ({signers.Count})");
        foreach (var signer in signers)
        {
            DrawSignerCard(ctx, signer);
            ctx.CursorY += 10;
        }
    }

    private static void DrawSignerCard(RenderContext ctx, CertificateSignerEntry signer)
    {
        var metaFont = new XFont(SansFamily, 8.5, XFontStyleEx.Regular);
        var labelFont = new XFont(SansFamily, 7.5, XFontStyleEx.Bold);

        // Timeline: solo los eventos que existen.
        var timeline = new List<(string Label, string Value)>();
        if (signer.FirstViewedAtUtc is { } viewed)
            timeline.Add(("Viewed", FormatUtc(viewed)));
        if (signer.ConsentAcceptedAtUtc is { } consent)
            timeline.Add(("Consent accepted", FormatUtc(consent)));
        if (signer.SignedAtUtc is { } signed)
            timeline.Add((signer.Status.ToString(), FormatUtc(signed)));
        else
            timeline.Add(("Status", signer.Status.ToString()));

        var innerWidth = ctx.ContentWidth - 24 - 30; // padding + bullet column
        var uaLines = string.IsNullOrEmpty(signer.UserAgent)
            ? 0
            : WrapLines(ctx.Gfx, signer.UserAgent, metaFont, innerWidth).Count;

        // Altura dinámica: cabecera (nombre+email) + filas de timeline + IP + UA.
        var height = 14 + 14 + 6 + timeline.Count * 13 + 13 + (uaLines > 0 ? 12 + uaLines * 11 : 0) + 14;

        ctx.EnsureSpace(height);
        var cardTop = ctx.CursorY;
        var textX = MarginLeft + 30;

        // Border
        ctx.Gfx.DrawRoundedRectangle(
            new XPen(CardBorder, 0.6),
            new XRect(MarginLeft, cardTop, ctx.ContentWidth, height),
            new XSize(6, 6)
        );

        // Bullet
        var bulletRect = new XRect(MarginLeft + 8, cardTop + 10, 20, 20);
        ctx.Gfx.DrawEllipse(new XSolidBrush(BrandPrimary), bulletRect);
        ctx.Gfx.DrawString(signer.Order.ToString(), labelFont, XBrushes.White, bulletRect, XStringFormats.Center);

        var y = cardTop + 20;
        ctx.Gfx.DrawString(
            EllipsizeToWidth(
                ctx.Gfx,
                signer.FullName,
                new XFont(SansFamily, 11, XFontStyleEx.Bold),
                ctx.ContentWidth - 150
            ),
            new XFont(SansFamily, 11, XFontStyleEx.Bold),
            new XSolidBrush(TextPrimary),
            new XPoint(textX, y)
        );

        // Status pill (right)
        DrawStatusPill(ctx, signer.Status, cardTop + 10);

        y += 14;
        ctx.Gfx.DrawString(
            EllipsizeToWidth(ctx.Gfx, signer.Email, metaFont, ctx.ContentWidth - 60),
            metaFont,
            new XSolidBrush(TextMuted),
            new XPoint(textX, y)
        );

        y += 8;
        foreach (var (label, val) in timeline)
        {
            y += 13;
            ctx.Gfx.DrawString(label.ToUpperInvariant(), labelFont, new XSolidBrush(TextMuted), new XPoint(textX, y));
            ctx.Gfx.DrawString(val, metaFont, new XSolidBrush(TextPrimary), new XPoint(textX + 130, y));
        }

        y += 13;
        ctx.Gfx.DrawString("CLIENT IP", labelFont, new XSolidBrush(TextMuted), new XPoint(textX, y));
        ctx.Gfx.DrawString(signer.ClientIp ?? "—", metaFont, new XSolidBrush(TextPrimary), new XPoint(textX + 130, y));

        if (uaLines > 0)
        {
            y += 12;
            ctx.Gfx.DrawString("USER AGENT", labelFont, new XSolidBrush(TextMuted), new XPoint(textX, y));
            y += 11;
            foreach (var line in WrapLines(ctx.Gfx, signer.UserAgent!, metaFont, innerWidth))
            {
                ctx.Gfx.DrawString(line, metaFont, new XSolidBrush(TextPrimary), new XPoint(textX, y));
                y += 11;
            }
        }

        ctx.CursorY = cardTop + height;
    }

    private static void DrawStatusPill(RenderContext ctx, SignerStatus status, double top)
    {
        var badgeFont = new XFont(SansFamily, 7.5, XFontStyleEx.Bold);
        var statusText = status.ToString().ToUpperInvariant();
        var bg = status switch
        {
            SignerStatus.Signed => XColor.FromArgb(212, 240, 220),
            SignerStatus.Rejected => XColor.FromArgb(248, 220, 220),
            _ => XColor.FromArgb(232, 232, 236),
        };
        var ink = status switch
        {
            SignerStatus.Signed => XColor.FromArgb(16, 92, 42),
            SignerStatus.Rejected => XColor.FromArgb(140, 30, 30),
            _ => XColor.FromArgb(80, 80, 90),
        };
        var size = ctx.Gfx.MeasureString(statusText, badgeFont);
        var rect = new XRect(ctx.ContentRight - size.Width - 20, top, size.Width + 12, 14);
        ctx.Gfx.DrawRoundedRectangle(new XSolidBrush(bg), rect, new XSize(7, 7));
        ctx.Gfx.DrawString(statusText, badgeFont, new XSolidBrush(ink), rect, XStringFormats.Center);
    }

    private static void WriteLegalFooter(RenderContext ctx, CertificateOfCompletionModel model)
    {
        var boldFont = new XFont(SansFamily, 7.5, XFontStyleEx.Bold);
        var italicFont = new XFont(SansFamily, 7.5, XFontStyleEx.Italic);

        ctx.EnsureSpace(52);
        ctx.CursorY += 6;
        ctx.Gfx.DrawLine(new XPen(RuleColor, 0.5), MarginLeft, ctx.CursorY, ctx.ContentRight, ctx.CursorY);
        ctx.CursorY += 12;

        ctx.Gfx.DrawString(
            $"Generated by {BrandProduct}",
            boldFont,
            new XSolidBrush(TextPrimary),
            new XPoint(MarginLeft, ctx.CursorY)
        );
        var rendered = $"Rendered {FormatUtc(DateTime.UtcNow)}";
        ctx.Gfx.DrawString(
            rendered,
            italicFont,
            new XSolidBrush(TextMuted),
            new XPoint(ctx.ContentRight - ctx.Gfx.MeasureString(rendered, italicFont).Width, ctx.CursorY)
        );

        ctx.CursorY += 12;
        var disclaimer =
            "This certificate is an integral part of the signed document and constitutes evidence of the "
            + "signature process under the ESIGN Act (15 U.S.C. §7001) and Regulation (EU) 910/2014 (eIDAS).";
        WriteWrapped(ctx, disclaimer, italicFont, TextMuted, MarginLeft, ctx.ContentWidth, lineHeight: 11);

        ctx.CursorY += 4;
        ctx.Gfx.DrawString("Reference: ", boldFont, new XSolidBrush(TextMuted), new XPoint(MarginLeft, ctx.CursorY));
        var w = ctx.Gfx.MeasureString("Reference: ", boldFont).Width;
        ctx.Gfx.DrawString(
            ShortReference(model.SignatureRequestId),
            new XFont(MonoFamily, 7.5, XFontStyleEx.Regular),
            new XSolidBrush(TextPrimary),
            new XPoint(MarginLeft + w, ctx.CursorY)
        );
    }

    // ------------------------------------------------------------------
    // Layout helpers
    // ------------------------------------------------------------------

    private static void WriteSectionHeader(RenderContext ctx, string title)
    {
        var font = new XFont(SansFamily, 9, XFontStyleEx.Bold);
        ctx.EnsureSpace(28);
        ctx.Gfx.DrawString(
            title.ToUpperInvariant(),
            font,
            new XSolidBrush(BrandAccent),
            new XPoint(MarginLeft, ctx.CursorY + 4)
        );
        ctx.CursorY += 8;
        ctx.Gfx.DrawLine(new XPen(RuleColor, 0.5), MarginLeft, ctx.CursorY, ctx.ContentRight, ctx.CursorY);
        ctx.CursorY += 14;
    }

    private static void WriteRow(RenderContext ctx, string label, string value)
    {
        var labelFont = new XFont(SansFamily, 8.5, XFontStyleEx.Bold);
        var valueFont = new XFont(SansFamily, 9, XFontStyleEx.Regular);
        var valueX = MarginLeft + LabelColWidth;
        var valueWidth = ctx.ContentRight - valueX;

        var lines = WrapLines(ctx.Gfx, value ?? string.Empty, valueFont, valueWidth);
        var rowHeight = Math.Max(15, lines.Count * 13 + 2);
        ctx.EnsureSpace(rowHeight);

        ctx.Gfx.DrawString(label, labelFont, new XSolidBrush(TextMuted), new XPoint(MarginLeft, ctx.CursorY + 9));
        var y = ctx.CursorY + 9;
        foreach (var line in lines)
        {
            ctx.Gfx.DrawString(line, valueFont, new XSolidBrush(TextPrimary), new XPoint(valueX, y));
            y += 13;
        }
        ctx.CursorY += rowHeight;
    }

    private static void WriteHashBlock(RenderContext ctx, string label, string hash)
    {
        var labelFont = new XFont(SansFamily, 8.5, XFontStyleEx.Bold);
        var monoFont = new XFont(MonoFamily, 8.5, XFontStyleEx.Regular);
        var chunked = FormatHashChunked(hash);
        var lines = WrapLines(ctx.Gfx, chunked, monoFont, ctx.ContentWidth);

        ctx.EnsureSpace(14 + lines.Count * 12 + 4);
        ctx.Gfx.DrawString(label, labelFont, new XSolidBrush(TextMuted), new XPoint(MarginLeft, ctx.CursorY + 9));
        ctx.CursorY += 13;
        foreach (var line in lines)
        {
            ctx.Gfx.DrawString(line, monoFont, new XSolidBrush(TextPrimary), new XPoint(MarginLeft, ctx.CursorY + 9));
            ctx.CursorY += 12;
        }
        ctx.CursorY += 6;
    }

    private static void WriteWrapped(
        RenderContext ctx,
        string text,
        XFont font,
        XColor color,
        double x,
        double width,
        double lineHeight
    )
    {
        var lines = WrapLines(ctx.Gfx, text, font, width);
        ctx.EnsureSpace(lines.Count * lineHeight);
        foreach (var line in lines)
        {
            ctx.Gfx.DrawString(line, font, new XSolidBrush(color), new XPoint(x, ctx.CursorY + 8));
            ctx.CursorY += lineHeight;
        }
    }

    private static double DrawLogo(XGraphics gfx, XImage? image, double x, double top)
    {
        if (image is null)
            return x;
        var width = LogoHeight * image.PixelWidth / Math.Max(1.0, image.PixelHeight);
        width = Math.Min(width, 180); // techo por si un logo viene muy apaisado
        gfx.DrawImage(image, x, top, width, LogoHeight);
        return x + width + 16;
    }

    private static byte[]? LoadEmbeddedPlatformLogo()
    {
        try
        {
            var asm = typeof(PdfSharpCertificateRenderer).Assembly;
            using var stream = asm.GetManifestResourceStream(
                "TaxVision.Signature.Infrastructure.Sealing.Assets.platform-logo.png"
            );
            if (stream is null)
                return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null; // sin logo embebido, el header cae al chip de texto
        }
    }

    private static XImage? TryLoadImage(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return null;
        try
        {
            return XImage.FromStream(new MemoryStream(bytes));
        }
        catch
        {
            return null; // un logo ilegible no debe tumbar el certificado
        }
    }

    // Envuelve por ancho medido; parte palabras largas (emails, hashes sin espacios) por carácter.
    private static List<string> WrapLines(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
            return result;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var pending = word;
            if (result.Count == 0)
                result.Add(string.Empty);

            var candidate = result[^1].Length == 0 ? pending : result[^1] + " " + pending;
            if (gfx.MeasureString(candidate, font).Width <= maxWidth)
            {
                result[^1] = candidate;
                continue;
            }

            // No cabe en la línea actual: nueva línea, partiendo la palabra si por sí sola excede.
            if (result[^1].Length > 0)
                result.Add(string.Empty);

            while (gfx.MeasureString(pending, font).Width > maxWidth && pending.Length > 1)
            {
                var cut = pending.Length;
                while (cut > 1 && gfx.MeasureString(pending[..cut], font).Width > maxWidth)
                    cut--;
                result[^1] = pending[..cut];
                result.Add(string.Empty);
                pending = pending[cut..];
            }
            result[^1] = pending;
        }
        return result;
    }

    private static string EllipsizeToWidth(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        if (string.IsNullOrEmpty(text) || gfx.MeasureString(text, font).Width <= maxWidth)
            return text;
        var cut = text.Length;
        while (cut > 1 && gfx.MeasureString(text[..cut] + "…", font).Width > maxWidth)
            cut--;
        return text[..cut] + "…";
    }

    private static string ShortReference(Guid id) => id.ToString("N")[..12].ToUpperInvariant();

    private static string FormatUtc(DateTime dt) => dt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

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

    // ------------------------------------------------------------------
    // Contexto de render con paginación
    // ------------------------------------------------------------------

    private sealed class RenderContext : IDisposable
    {
        private readonly PdfDocument _pdf;

        public RenderContext(PdfDocument pdf)
        {
            _pdf = pdf;
            NewPage();
        }

        public PdfPage Page { get; private set; } = null!;
        public XGraphics Gfx { get; private set; } = null!;
        public double CursorY { get; set; }

        public double ContentRight => Page.Width.Point - MarginRight;
        public double ContentWidth => ContentRight - MarginLeft;

        public void EnsureSpace(double needed)
        {
            if (CursorY + needed > Page.Height.Point - MarginBottom)
                NewPage();
        }

        private void NewPage()
        {
            Gfx?.Dispose();
            Page = _pdf.AddPage();
            Page.Size = PageSize.A4;
            Gfx = XGraphics.FromPdfPage(Page);
            CursorY = MarginTop;
        }

        public void Dispose() => Gfx?.Dispose();
    }
}
