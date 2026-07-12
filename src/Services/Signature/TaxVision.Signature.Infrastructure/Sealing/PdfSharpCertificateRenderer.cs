using System.Security.Cryptography;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using TaxVision.Signature.Application.Abstractions.Sealing;

namespace TaxVision.Signature.Infrastructure.Sealing;

/// <summary>
/// Professional Certificate of Completion renderer, modeled after industry standards
/// (DocuSign, Adobe Sign, HelloSign): brand header, tenant + reference block, integrity
/// hashes formatted in monospaced 8-char groups, chronological audit trail per signer,
/// and a legal disclaimer footer. IDs and hashes are shown in full because ESIGN Act
/// §101(d)(1) and eIDAS Annex IV require immutable, non-repudiable identifiers.
/// </summary>
public sealed class PdfSharpCertificateRenderer : ICertificateOfCompletionRenderer
{
    private const string BrandName = "TaxProCore";
    private const string BrandProduct = "TaxProCore Signature Service";

    private const double MarginLeft = 48;
    private const double MarginRight = 48;
    private const double MarginTop = 48;

    private const string SansFamily = "Helvetica";
    private const string MonoFamily = "Courier";

    // Brand palette (deep slate blue + neutrals) — sober, print-safe.
    private static readonly XColor BrandPrimary = XColor.FromArgb(20, 45, 90);
    private static readonly XColor BrandAccent = XColor.FromArgb(0, 122, 132);
    private static readonly XColor TextPrimary = XColor.FromArgb(24, 30, 42);
    private static readonly XColor TextMuted = XColor.FromArgb(96, 105, 120);
    private static readonly XColor RuleColor = XColor.FromArgb(210, 215, 225);
    private static readonly XColor PanelBg = XColor.FromArgb(247, 249, 252);
    private static readonly XColor PanelBorder = XColor.FromArgb(220, 226, 236);

    public CertificateResult Render(CertificateOfCompletionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        using var pdf = new PdfDocument();
        pdf.Info.Title = $"{BrandName} — Certificate of Completion";
        pdf.Info.Author = BrandProduct;
        pdf.Info.Subject = $"Legal audit trail for signature request {model.SignatureRequestId:D}";
        pdf.Info.Keywords = "e-signature; audit trail; ESIGN; eIDAS; PAdES-B";

        var page = pdf.AddPage();
        page.Size = PageSize.A4;

        using var gfx = XGraphics.FromPdfPage(page);

        var cursorY = WriteBrandHeader(gfx, page, MarginTop);
        cursorY = WriteReferencePanel(gfx, page, model, cursorY + 4);
        cursorY = WriteDocumentPanel(gfx, page, model, cursorY + 14);
        cursorY = WriteIntegrityPanel(gfx, page, model, cursorY + 14);
        cursorY = WriteSignersSection(gfx, page, model.Signers, cursorY + 14);
        WriteLegalFooter(gfx, page, model);

        using var output = new MemoryStream();
        pdf.Save(output, closeStream: false);
        var bytes = output.ToArray();
        return new CertificateResult(bytes, ComputeSha256(bytes));
    }

    // ------------------------------------------------------------------
    // Sections
    // ------------------------------------------------------------------

    private static double WriteBrandHeader(XGraphics gfx, PdfPage page, double cursorY)
    {
        var brandFont = new XFont(SansFamily, 9, XFontStyleEx.Bold);
        var titleFont = new XFont(SansFamily, 22, XFontStyleEx.Bold);
        var subtitleFont = new XFont(SansFamily, 10, XFontStyleEx.Regular);

        var contentRight = page.Width.Point - MarginRight;

        // Brand chip (top-left)
        var chipRect = new XRect(MarginLeft, cursorY, 88, 18);
        gfx.DrawRoundedRectangle(new XSolidBrush(BrandPrimary), chipRect, new XSize(6, 6));
        gfx.DrawString(
            BrandName,
            brandFont,
            XBrushes.White,
            new XRect(chipRect.X, chipRect.Y + 4, chipRect.Width, chipRect.Height),
            XStringFormats.TopCenter
        );

        // Legal audit trail tag (top-right)
        var badgeFont = new XFont(SansFamily, 7, XFontStyleEx.Bold);
        var badgeText = "LEGAL AUDIT TRAIL  •  ESIGN / eIDAS";
        var badgeSize = gfx.MeasureString(badgeText, badgeFont);
        gfx.DrawString(
            badgeText,
            badgeFont,
            new XSolidBrush(TextMuted),
            new XPoint(contentRight - badgeSize.Width, cursorY + 12)
        );

        cursorY += 42;
        gfx.DrawString(
            "Certificate of Completion",
            titleFont,
            new XSolidBrush(TextPrimary),
            new XPoint(MarginLeft, cursorY)
        );
        cursorY += 22;
        gfx.DrawString(
            "This document certifies the events of the electronic signature process below.",
            subtitleFont,
            new XSolidBrush(TextMuted),
            new XPoint(MarginLeft, cursorY)
        );
        cursorY += 12;

        gfx.DrawLine(new XPen(BrandPrimary, 1.2), MarginLeft, cursorY + 4, contentRight, cursorY + 4);
        return cursorY + 10;
    }

    private static double WriteReferencePanel(
        XGraphics gfx,
        PdfPage page,
        CertificateOfCompletionModel model,
        double cursorY
    )
    {
        const double PanelHeight = 54;
        var panelTop = DrawPanelChrome(gfx, page, cursorY, "Reference", PanelHeight);
        var y = panelTop + 18;
        y = DrawLabelValue(gfx, "Signature Request ID", model.SignatureRequestId.ToString("D"), y, mono: true);
        _ = DrawLabelValue(gfx, "Tenant ID", model.TenantId.ToString("D"), y, mono: true);
        return panelTop + PanelHeight;
    }

    private static double WriteDocumentPanel(
        XGraphics gfx,
        PdfPage page,
        CertificateOfCompletionModel model,
        double cursorY
    )
    {
        const double PanelHeight = 84;
        var panelTop = DrawPanelChrome(gfx, page, cursorY, "Document", PanelHeight);
        var y = panelTop + 18;
        y = DrawLabelValue(gfx, "Title", model.Title, y);
        y = DrawLabelValue(gfx, "Category", model.Category.ToString(), y);
        y = DrawLabelValue(gfx, "Created (UTC)", FormatUtc(model.CreatedAtUtc), y);
        _ = DrawLabelValue(gfx, "Completed (UTC)", FormatUtc(model.CompletedAtUtc), y);
        return panelTop + PanelHeight;
    }

    private static double WriteIntegrityPanel(
        XGraphics gfx,
        PdfPage page,
        CertificateOfCompletionModel model,
        double cursorY
    )
    {
        const double PanelHeight = 92;
        var panelTop = DrawPanelChrome(gfx, page, cursorY, "Document Integrity  •  SHA-256", PanelHeight);

        var monoFont = new XFont(MonoFamily, 8.5, XFontStyleEx.Regular);
        var labelFont = new XFont(SansFamily, 8.5, XFontStyleEx.Bold);
        var noteFont = new XFont(SansFamily, 7.5, XFontStyleEx.Italic);
        var muted = new XSolidBrush(TextMuted);
        var value = new XSolidBrush(TextPrimary);

        var y = panelTop + 16;
        gfx.DrawString("Original document", labelFont, muted, new XPoint(MarginLeft + 12, y));
        y += 12;
        gfx.DrawString(FormatHashChunked(model.DocumentHashPre), monoFont, value, new XPoint(MarginLeft + 12, y));
        y += 16;

        gfx.DrawString("Sealed document (after signatures)", labelFont, muted, new XPoint(MarginLeft + 12, y));
        y += 12;
        gfx.DrawString(FormatHashChunked(model.DocumentHashPost), monoFont, value, new XPoint(MarginLeft + 12, y));
        y += 14;

        gfx.DrawString(
            "The pair of SHA-256 hashes above is the immutable evidence of document integrity.",
            noteFont,
            muted,
            new XPoint(MarginLeft + 12, y)
        );

        return panelTop + PanelHeight;
    }

    private static double WriteSignersSection(
        XGraphics gfx,
        PdfPage page,
        IReadOnlyList<CertificateSignerEntry> signers,
        double cursorY
    )
    {
        var sectionFont = new XFont(SansFamily, 11, XFontStyleEx.Bold);
        gfx.DrawString(
            $"Signers  ({signers.Count})",
            sectionFont,
            new XSolidBrush(TextPrimary),
            new XPoint(MarginLeft, cursorY)
        );
        cursorY += 6;
        gfx.DrawLine(new XPen(RuleColor, 0.5), MarginLeft, cursorY + 4, page.Width.Point - MarginRight, cursorY + 4);
        cursorY += 14;

        var order = 1;
        foreach (var signer in signers)
        {
            cursorY = DrawSignerCard(gfx, page, signer, order++, cursorY);
            cursorY += 10;
        }
        return cursorY;
    }

    private static double DrawSignerCard(
        XGraphics gfx,
        PdfPage page,
        CertificateSignerEntry signer,
        int index,
        double cursorY
    )
    {
        var contentRight = page.Width.Point - MarginRight;
        var nameFont = new XFont(SansFamily, 11, XFontStyleEx.Bold);
        var metaFont = new XFont(SansFamily, 8.5, XFontStyleEx.Regular);
        var labelFont = new XFont(SansFamily, 7.5, XFontStyleEx.Bold);
        var badgeFont = new XFont(SansFamily, 7.5, XFontStyleEx.Bold);
        var mutedBrush = new XSolidBrush(TextMuted);
        var primaryBrush = new XSolidBrush(TextPrimary);

        var cardTop = cursorY;
        // Index bullet
        var bulletRect = new XRect(MarginLeft, cardTop + 2, 20, 20);
        gfx.DrawEllipse(new XSolidBrush(BrandPrimary), bulletRect);
        gfx.DrawString(index.ToString(), badgeFont, XBrushes.White, bulletRect, XStringFormats.Center);

        // Name + email row
        var textX = MarginLeft + 30;
        gfx.DrawString(signer.FullName, nameFont, primaryBrush, new XPoint(textX, cardTop + 12));
        gfx.DrawString(signer.Email, metaFont, mutedBrush, new XPoint(textX, cardTop + 26));

        // Status pill (right side)
        var statusText = signer.Status.ToString().ToUpperInvariant();
        var statusColor = signer.Status.ToString() switch
        {
            "Signed" => XColor.FromArgb(212, 240, 220),
            "Rejected" => XColor.FromArgb(248, 220, 220),
            _ => XColor.FromArgb(232, 232, 236),
        };
        var statusInk = signer.Status.ToString() switch
        {
            "Signed" => XColor.FromArgb(16, 92, 42),
            "Rejected" => XColor.FromArgb(140, 30, 30),
            _ => XColor.FromArgb(80, 80, 90),
        };
        var pillSize = gfx.MeasureString(statusText, badgeFont);
        var pillRect = new XRect(contentRight - pillSize.Width - 12, cardTop + 4, pillSize.Width + 12, 14);
        gfx.DrawRoundedRectangle(new XSolidBrush(statusColor), pillRect, new XSize(7, 7));
        gfx.DrawString(statusText, badgeFont, new XSolidBrush(statusInk), pillRect, XStringFormats.Center);

        // Meta grid (signed at + client IP + user agent)
        var gridY = cardTop + 40;
        var col1X = textX;
        var col2X = textX + 200;
        gfx.DrawString("SIGNED (UTC)", labelFont, mutedBrush, new XPoint(col1X, gridY));
        gfx.DrawString(
            signer.SignedAtUtc is { } dt ? FormatUtc(dt) : "—",
            metaFont,
            primaryBrush,
            new XPoint(col1X, gridY + 12)
        );

        gfx.DrawString("CLIENT IP", labelFont, mutedBrush, new XPoint(col2X, gridY));
        gfx.DrawString(signer.ClientIp ?? "—", metaFont, primaryBrush, new XPoint(col2X, gridY + 12));

        // User agent (full width, one line, truncated)
        if (!string.IsNullOrEmpty(signer.UserAgent))
        {
            gfx.DrawString("USER AGENT", labelFont, mutedBrush, new XPoint(col1X, gridY + 30));
            gfx.DrawString(TrimTo(signer.UserAgent, 100), metaFont, primaryBrush, new XPoint(col1X, gridY + 42));
        }

        var cardHeight = string.IsNullOrEmpty(signer.UserAgent) ? 70 : 88;
        // Card border
        gfx.DrawRoundedRectangle(
            new XPen(PanelBorder, 0.6),
            new XSolidBrush(XColor.FromArgb(0, 255, 255, 255)),
            new XRect(MarginLeft, cardTop, page.Width.Point - MarginRight - MarginLeft, cardHeight),
            new XSize(6, 6)
        );

        return cardTop + cardHeight;
    }

    private static void WriteLegalFooter(XGraphics gfx, PdfPage page, CertificateOfCompletionModel model)
    {
        var footerRuleY = page.Height.Point - 60;
        gfx.DrawLine(new XPen(RuleColor, 0.5), MarginLeft, footerRuleY, page.Width.Point - MarginRight, footerRuleY);

        var boldFont = new XFont(SansFamily, 7.5, XFontStyleEx.Bold);
        var italicFont = new XFont(SansFamily, 7.5, XFontStyleEx.Italic);
        var monoSmall = new XFont(MonoFamily, 7.5, XFontStyleEx.Regular);
        var muted = new XSolidBrush(TextMuted);
        var text = new XSolidBrush(TextPrimary);

        var y = footerRuleY + 12;
        gfx.DrawString($"Generated by {BrandProduct}", boldFont, text, new XPoint(MarginLeft, y));
        gfx.DrawString(
            $"Rendered {FormatUtc(DateTime.UtcNow)}",
            italicFont,
            muted,
            new XPoint(
                page.Width.Point
                    - MarginRight
                    - gfx.MeasureString($"Rendered {FormatUtc(DateTime.UtcNow)}", italicFont).Width,
                y
            )
        );

        y += 12;
        var disclaimer =
            "This certificate is an integral part of the signed document and constitutes evidence of the "
            + "signature process under the ESIGN Act (15 U.S.C. §7001) and Regulation (EU) 910/2014 (eIDAS).";
        gfx.DrawString(disclaimer, italicFont, muted, new XPoint(MarginLeft, y));

        y += 16;
        gfx.DrawString("Reference:", boldFont, muted, new XPoint(MarginLeft, y));
        gfx.DrawString(model.SignatureRequestId.ToString("D"), monoSmall, text, new XPoint(MarginLeft + 46, y));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Draws the section title above and the rounded panel frame; returns the Y of the panel top
    /// (inside the frame) so the caller can lay its content on top.
    /// </summary>
    private static double DrawPanelChrome(XGraphics gfx, PdfPage page, double cursorY, string title, double panelHeight)
    {
        var contentRight = page.Width.Point - MarginRight;
        var titleFont = new XFont(SansFamily, 9, XFontStyleEx.Bold);

        gfx.DrawString(
            title.ToUpperInvariant(),
            titleFont,
            new XSolidBrush(BrandAccent),
            new XPoint(MarginLeft, cursorY)
        );
        var panelTop = cursorY + 12;

        var rect = new XRect(MarginLeft, panelTop, contentRight - MarginLeft, panelHeight);
        gfx.DrawRoundedRectangle(new XPen(PanelBorder, 0.6), new XSolidBrush(PanelBg), rect, new XSize(6, 6));
        return panelTop;
    }

    private static double DrawLabelValue(XGraphics gfx, string label, string value, double y, bool mono = false)
    {
        var labelFont = new XFont(SansFamily, 8.5, XFontStyleEx.Bold);
        var valueFont = mono
            ? new XFont(MonoFamily, 8.5, XFontStyleEx.Regular)
            : new XFont(SansFamily, 9, XFontStyleEx.Regular);
        gfx.DrawString(label, labelFont, new XSolidBrush(TextMuted), new XPoint(MarginLeft + 12, y));
        gfx.DrawString(value ?? string.Empty, valueFont, new XSolidBrush(TextPrimary), new XPoint(MarginLeft + 140, y));
        return y + 15;
    }

    private static string FormatUtc(DateTime dt) => dt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

    /// <summary>Groups a hex hash into 8-char blocks for readability (DocuSign / Adobe Sign convention).</summary>
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

    private static string TrimTo(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length > max ? value[..max] + "…" : value);

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
