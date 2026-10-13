using System.Security.Cryptography;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Sealing;

/// <summary>
/// Certificate of Completion profesional (estilo DocuSign / Adobe Sign / Click&amp;Sign): marca del tenant
/// plataforma (logo del tenant branding), atestación formal, resumen del envelope en rejilla, documentos
/// en tarjetas con hashes SHA-256 e integridad, audit trail por firmante con timeline visual (visto →
/// consentimiento → verificado → firmado, IP, método de verificación/captura, user agent), consentimiento
/// ESIGN/UETA, panel de sello criptográfico con los datos reales del certificado y footer legal. Pagina de
/// verdad: nunca desborda el margen. No expone identificadores internos (TenantId): solo la referencia.
/// </summary>
public sealed class PdfSharpCertificateRenderer : ICertificateOfCompletionRenderer
{
    // La marca (nombre + logo) es la del tenant plataforma, que llega en el modelo (model.PlatformLogo);
    // el texto solo es respaldo si no hay logo. No se estampa "Signature Service": basta el logo/marca.
    private const string BrandName = "TaxProffice";

    private const double MarginLeft = 48;
    private const double MarginRight = 48;
    private const double MarginTop = 54;
    private const double MarginBottom = 54;
    private const double LogoHeight = 30;

    private const string SansFamily = "Helvetica";
    private const string SerifFamily = "Times";
    private const string MonoFamily = "Courier";

    private static readonly XColor BrandPrimary = XColor.FromArgb(30, 70, 107);
    private static readonly XColor BrandAccent = XColor.FromArgb(0, 122, 132);
    private static readonly XColor TextPrimary = XColor.FromArgb(24, 30, 42);
    private static readonly XColor TextMuted = XColor.FromArgb(96, 105, 120);
    private static readonly XColor Faint = XColor.FromArgb(139, 147, 163);
    private static readonly XColor RuleColor = XColor.FromArgb(210, 215, 225);
    private static readonly XColor CardBorder = XColor.FromArgb(220, 226, 236);
    private static readonly XColor PaperTint = XColor.FromArgb(247, 249, 251);
    private static readonly XColor Band = XColor.FromArgb(238, 243, 247);
    private static readonly XColor SealBg = XColor.FromArgb(242, 247, 246);
    private static readonly XColor SealBorder = XColor.FromArgb(150, 198, 202);
    private static readonly XColor AccentTint = XColor.FromArgb(230, 241, 242);
    private static readonly XColor OkBg = XColor.FromArgb(212, 240, 220);
    private static readonly XColor OkInk = XColor.FromArgb(15, 92, 42);
    private static readonly XColor RejectBg = XColor.FromArgb(248, 220, 220);
    private static readonly XColor RejectInk = XColor.FromArgb(140, 30, 30);
    private static readonly XColor NeutralBg = XColor.FromArgb(232, 232, 236);
    private static readonly XColor NeutralInk = XColor.FromArgb(80, 80, 90);

    // Logo de plataforma embebido (recurso del ensamblado). Se usa cuando el modelo no trae uno propio.
    private static readonly byte[]? PlatformLogoBytes = LoadEmbeddedPlatformLogo();

    public CertificateResult Render(CertificateOfCompletionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        using var pdf = new PdfDocument();
        pdf.Info.Title = $"{BrandName} — Certificate of Completion";
        pdf.Info.Author = BrandName;
        pdf.Info.Subject = $"Legal audit trail for signature request {model.SignatureRequestId:D}";
        pdf.Info.Keywords = "e-signature; audit trail; ESIGN; UETA; eIDAS; PAdES-B";

        var platformSource = model.PlatformLogo ?? PlatformLogoBytes;
        XImage? platformLogo = TryLoadImage(platformSource);
        try
        {
            using var ctx = new RenderContext(pdf);

            WriteHeader(ctx, model, platformLogo);
            WriteAttestation(ctx, model);
            WriteSummary(ctx, model);
            WriteDocuments(ctx, model.Documents);
            WriteSigners(ctx, model.SignersGlobal);
            WritePreparer(ctx, model.Preparer);
            WriteConsent(ctx);
            WriteSeal(ctx, model.Seal);
            WriteLegalFooter(ctx, model);

            using var output = new MemoryStream();
            pdf.Save(output, closeStream: false);
            var bytes = output.ToArray();
            return new CertificateResult(bytes, ComputeSha256(bytes));
        }
        finally
        {
            platformLogo?.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Header
    // ------------------------------------------------------------------

    private static void WriteHeader(RenderContext ctx, CertificateOfCompletionModel model, XImage? platformLogo)
    {
        var gfx = ctx.Gfx;
        var titleFont = new XFont(SerifFamily, 27, XFontStyleEx.Bold);
        var subtitleFont = new XFont(SansFamily, 10, XFontStyleEx.Regular);
        var badgeFont = new XFont(SansFamily, 7, XFontStyleEx.Bold);

        var top = ctx.CursorY;

        if (platformLogo is not null)
        {
            DrawLogo(gfx, platformLogo, MarginLeft, top);
        }
        else
        {
            var chipRect = new XRect(MarginLeft, top, 92, 20);
            gfx.DrawRoundedRectangle(new XSolidBrush(BrandPrimary), chipRect, new XSize(7, 7));
            gfx.DrawString(
                BrandName,
                new XFont(SansFamily, 9.5, XFontStyleEx.Bold),
                XBrushes.White,
                new XRect(chipRect.X, chipRect.Y + 5, chipRect.Width, chipRect.Height),
                XStringFormats.TopCenter
            );
        }

        var badgeText = "LEGAL AUDIT TRAIL  •  ESIGN · UETA · PAdES-B";
        var badgeSize = gfx.MeasureString(badgeText, badgeFont);
        gfx.DrawString(
            badgeText,
            badgeFont,
            new XSolidBrush(TextMuted),
            new XPoint(ctx.ContentRight - badgeSize.Width, top + 12)
        );

        ctx.CursorY = top + LogoHeight + 18;
        gfx.DrawString(
            "Certificate of Completion",
            titleFont,
            new XSolidBrush(TextPrimary),
            new XPoint(MarginLeft, ctx.CursorY)
        );
        ctx.CursorY += 10;

        if (!string.IsNullOrWhiteSpace(model.Title))
        {
            ctx.CursorY += 10;
            WriteWrapped(
                ctx,
                model.Title,
                new XFont(SansFamily, 12, XFontStyleEx.Bold),
                TextPrimary,
                MarginLeft,
                ctx.ContentWidth,
                15
            );
            ctx.CursorY += 4;
        }
        gfx.DrawString(
            "This document certifies the events of the electronic signature process recorded below.",
            subtitleFont,
            new XSolidBrush(TextMuted),
            new XPoint(MarginLeft, ctx.CursorY + 8)
        );
        ctx.CursorY += 14;

        if (!string.IsNullOrWhiteSpace(model.IssuerName))
        {
            ctx.CursorY += 12;
            var issuerLabel = new XFont(SansFamily, 9, XFontStyleEx.Bold);
            gfx.DrawString("Issued by ", issuerLabel, new XSolidBrush(TextMuted), new XPoint(MarginLeft, ctx.CursorY));
            var w = gfx.MeasureString("Issued by ", issuerLabel).Width;
            gfx.DrawString(
                model.IssuerName,
                issuerLabel,
                new XSolidBrush(BrandPrimary),
                new XPoint(MarginLeft + w, ctx.CursorY)
            );
        }

        ctx.CursorY += 14;
        var idLabelFont = new XFont(SansFamily, 8.5, XFontStyleEx.Bold);
        gfx.DrawString(
            "Certificate ID: ",
            idLabelFont,
            new XSolidBrush(TextMuted),
            new XPoint(MarginLeft, ctx.CursorY)
        );
        var idLabelWidth = gfx.MeasureString("Certificate ID: ", idLabelFont).Width;
        gfx.DrawString(
            CertificateId(model.SignatureRequestId, model.CompletedAtUtc),
            new XFont(MonoFamily, 8.5, XFontStyleEx.Regular),
            new XSolidBrush(TextPrimary),
            new XPoint(MarginLeft + idLabelWidth, ctx.CursorY)
        );

        ctx.CursorY += 8;
        gfx.DrawLine(new XPen(BrandPrimary, 1.4), MarginLeft, ctx.CursorY, ctx.ContentRight, ctx.CursorY);
        ctx.CursorY += 16;
    }

    private static string CertificateId(Guid requestId, DateTime completedAtUtc)
    {
        var n = requestId.ToString("N").ToUpperInvariant();
        var year = completedAtUtc.ToUniversalTime().Year;
        return $"TPO-{year}-{n[..12]}-{n.Substring(12, 4)}";
    }

    // ------------------------------------------------------------------
    // Attestation (tinted box with accent bar)
    // ------------------------------------------------------------------

    private static void WriteAttestation(RenderContext ctx, CertificateOfCompletionModel model)
    {
        var font = new XFont(SansFamily, 9.5, XFontStyleEx.Regular);
        var issuer = string.IsNullOrWhiteSpace(model.IssuerName) ? "the issuing office" : model.IssuerName;
        var text =
            $"{BrandName} certifies that it generated and holds in its records the electronic evidence of the signature "
            + $"process described in this certificate, carried out on behalf of {issuer}. Each signer reviewed the "
            + "documents, accepted the electronic-records consent, completed identity verification, and applied their "
            + "signature. Upon completion every document was sealed with a digital signature and a SHA-256 integrity "
            + "hash, listed below, so any later change to a sealed file can be detected.";

        const double padX = 16;
        const double padY = 12;
        var innerWidth = ctx.ContentWidth - padX * 2 - 4;
        var lines = WrapLines(ctx.Gfx, text, font, innerWidth);
        var boxHeight = padY * 2 + lines.Count * 13;

        ctx.EnsureSpace(boxHeight + 6);
        var boxTop = ctx.CursorY;
        var rect = new XRect(MarginLeft, boxTop, ctx.ContentWidth, boxHeight);
        ctx.Gfx.DrawRoundedRectangle(new XSolidBrush(PaperTint), rect, new XSize(10, 10));
        // Filete de acento a la izquierda (como el mockup).
        ctx.Gfx.DrawLine(
            new XPen(BrandAccent, 2.5),
            MarginLeft + 1.2,
            boxTop + 3,
            MarginLeft + 1.2,
            boxTop + boxHeight - 3
        );

        var y = boxTop + padY + 9;
        foreach (var line in lines)
        {
            ctx.Gfx.DrawString(line, font, new XSolidBrush(TextPrimary), new XPoint(MarginLeft + padX + 4, y));
            y += 13;
        }
        ctx.CursorY = boxTop + boxHeight + 14;
    }

    // ------------------------------------------------------------------
    // Envelope summary (2-column grid)
    // ------------------------------------------------------------------

    private static void WriteSummary(RenderContext ctx, CertificateOfCompletionModel model)
    {
        WriteSectionHeader(ctx, "Envelope summary");

        var standard = model.Seal is { } seal ? $"PAdES-B · {seal.SignatureAlgorithm}" : "SHA-256 integrity hashing";
        var pairs = new (string Label, string Value)[]
        {
            ("Reference", ShortReference(model.SignatureRequestId)),
            ("Status", "Completed"),
            ("Category", model.Category),
            ("Signature standard", standard),
            ("Documents", model.Documents.Count.ToString()),
            ("Signers", model.SignersGlobal.Count.ToString()),
            ("Created (UTC)", FormatUtc(model.CreatedAtUtc)),
            ("Completed (UTC)", FormatUtc(model.CompletedAtUtc)),
        };
        DrawTwoColumnGrid(ctx, pairs);
        ctx.CursorY += 8;
    }

    // ------------------------------------------------------------------
    // Documents (cards)
    // ------------------------------------------------------------------

    private static void WriteDocuments(RenderContext ctx, IReadOnlyList<CertificateDocumentEntry> documents)
    {
        var ordered = documents.OrderBy(entry => entry.Order).ToList();
        // Mantener el encabezado junto a su primera tarjeta (no dejarlo huérfano al final de la página).
        if (ordered.Count > 0)
            ctx.EnsureSpace(28 + DocumentCardHeight(ctx, ordered[0], ordered.Count));
        WriteSectionHeader(ctx, $"Documents  ({documents.Count})");
        foreach (var document in ordered)
            DrawDocumentCard(ctx, document, documents.Count);

        var noteFont = new XFont(SansFamily, 7.5, XFontStyleEx.Italic);
        var note =
            "To verify a document, recompute the SHA-256 of its sealed PDF and compare it with that document's sealed hash above. "
            + "A match proves that specific document has not been altered since signing.";
        WriteWrapped(ctx, note, noteFont, TextMuted, MarginLeft, ctx.ContentWidth, 11);
        ctx.CursorY += 8;
    }

    private static double DocumentCardHeight(RenderContext ctx, CertificateDocumentEntry document, int totalDocs)
    {
        const double padX = 16;
        const double padTop = 14;
        var innerWidth = ctx.ContentWidth - padX * 2;
        var titleFont = new XFont(SansFamily, 11.5, XFontStyleEx.Bold);
        var monoFont = new XFont(MonoFamily, 8.5, XFontStyleEx.Regular);
        var titleLines = WrapLines(ctx.Gfx, document.Title, titleFont, innerWidth - 90).Count;
        var preLines = WrapLines(ctx.Gfx, FormatHashChunked(document.HashPre), monoFont, innerWidth).Count;
        var postLines = WrapLines(ctx.Gfx, FormatHashChunked(document.HashPost), monoFont, innerWidth).Count;
        _ = totalDocs;
        return padTop
            + titleLines * 14
            + 6
            + 15 * 2 // reference + sealed rows
            + 6
            + (13 + preLines * 12 + 6)
            + (13 + postLines * 12 + 6)
            + 10 // dashed separator
            + 14 // participants label
            + document.Signers.Count * 26
            + 12;
    }

    private static void DrawDocumentCard(RenderContext ctx, CertificateDocumentEntry document, int totalDocs)
    {
        const double padX = 16;
        const double padTop = 14;
        var innerWidth = ctx.ContentWidth - padX * 2;

        var titleFont = new XFont(SansFamily, 11.5, XFontStyleEx.Bold);
        var labelFont = new XFont(SansFamily, 8.5, XFontStyleEx.Bold);
        var valueFont = new XFont(SansFamily, 8.5, XFontStyleEx.Regular);
        var monoFont = new XFont(MonoFamily, 8.5, XFontStyleEx.Regular);

        var titleLines = WrapLines(ctx.Gfx, document.Title, titleFont, innerWidth - 90);
        var preLines = WrapLines(ctx.Gfx, FormatHashChunked(document.HashPre), monoFont, innerWidth);
        var postLines = WrapLines(ctx.Gfx, FormatHashChunked(document.HashPost), monoFont, innerWidth);

        var height = DocumentCardHeight(ctx, document, totalDocs);
        ctx.EnsureSpace(height);
        var top = ctx.CursorY;
        ctx.Gfx.DrawRoundedRectangle(
            new XPen(CardBorder, 0.8),
            new XRect(MarginLeft, top, ctx.ContentWidth, height),
            new XSize(12, 12)
        );

        var x = MarginLeft + padX;
        var y = top + padTop;

        // Title + "DOCUMENT n OF m"
        var badge = $"DOCUMENT {document.Order} OF {totalDocs}";
        var badgeFont = new XFont(SansFamily, 7.5, XFontStyleEx.Bold);
        var badgeW = ctx.Gfx.MeasureString(badge, badgeFont).Width;
        ctx.Gfx.DrawString(
            badge,
            badgeFont,
            new XSolidBrush(Faint),
            new XPoint(ctx.ContentRight - padX - badgeW, y + 8)
        );
        foreach (var line in titleLines)
        {
            ctx.Gfx.DrawString(line, titleFont, new XSolidBrush(TextPrimary), new XPoint(x, y + 9));
            y += 14;
        }
        y += 6;

        // Reference + Sealed (two narrow rows)
        DrawInlineKv(ctx, "Document reference", ShortReference(document.DocumentId), x, y, monoValue: true);
        y += 15;
        DrawInlineKv(ctx, "Sealed (UTC)", FormatUtc(document.SealedAtUtc), x, y, monoValue: false);
        y += 15 + 6;

        // Hashes
        y = DrawHashLines(ctx, "Original SHA-256", preLines, x, y, innerWidth);
        y = DrawHashLines(ctx, "Sealed SHA-256", postLines, x, y, innerWidth);

        // Dashed separator
        DrawDashedLine(ctx.Gfx, x, y + 2, ctx.ContentRight - padX, y + 2);
        y += 10;

        // Participants
        ctx.Gfx.DrawString(
            $"PARTICIPANTS ({document.Signers.Count})",
            new XFont(SansFamily, 7.5, XFontStyleEx.Bold),
            new XSolidBrush(BrandAccent),
            new XPoint(x, y + 9)
        );
        y += 14;
        foreach (var signer in document.Signers)
        {
            ctx.Gfx.DrawString(
                EllipsizeToWidth(ctx.Gfx, signer.FullName, labelFont, innerWidth),
                labelFont,
                new XSolidBrush(TextPrimary),
                new XPoint(x, y + 9)
            );
            y += 13;
            var evidence = $"Signed {FormatUtc(signer.SignedAtUtc)}  ·  IP {signer.ClientIp ?? "—"}";
            ctx.Gfx.DrawString(
                EllipsizeToWidth(ctx.Gfx, evidence, valueFont, innerWidth),
                valueFont,
                new XSolidBrush(TextMuted),
                new XPoint(x, y + 9)
            );
            y += 13;
        }

        ctx.CursorY = top + height + 12;
    }

    private static double DrawHashLines(
        RenderContext ctx,
        string label,
        List<string> lines,
        double x,
        double y,
        double width
    )
    {
        ctx.Gfx.DrawString(
            label,
            new XFont(SansFamily, 8.5, XFontStyleEx.Bold),
            new XSolidBrush(TextMuted),
            new XPoint(x, y + 9)
        );
        y += 13;
        var monoFont = new XFont(MonoFamily, 8.5, XFontStyleEx.Regular);
        foreach (var line in lines)
        {
            ctx.Gfx.DrawString(line, monoFont, new XSolidBrush(TextPrimary), new XPoint(x, y + 9));
            y += 12;
        }
        return y + 6;
    }

    // ------------------------------------------------------------------
    // Signers (cards with header band + visual timeline)
    // ------------------------------------------------------------------

    private static void WriteSigners(RenderContext ctx, IReadOnlyList<CertificateSignerEntry> signers)
    {
        // Mantener el encabezado junto a su primera tarjeta: si no caben, saltar de página ANTES del
        // encabezado para no dejarlo huérfano al final de la página.
        if (signers.Count > 0)
            ctx.EnsureSpace(28 + SignerLayout(ctx, signers[0]).Height);
        WriteSectionHeader(ctx, $"Signers & audit trail  ({signers.Count})");
        foreach (var signer in signers)
        {
            DrawSignerCard(ctx, signer);
            ctx.CursorY += 12;
        }
    }

    private static (
        List<(string Label, string Value)> Timeline,
        List<(string Label, string Value)> Meta,
        int UaLines,
        int MetaRows,
        double Height
    ) SignerLayout(RenderContext ctx, CertificateSignerEntry signer)
    {
        var metaFont = new XFont(SansFamily, 8.5, XFontStyleEx.Regular);

        var timeline = new List<(string Label, string Value)>();
        if (signer.FirstViewedAtUtc is { } viewed)
            timeline.Add(("VIEWED", FormatUtc(viewed)));
        if (signer.ConsentAcceptedAtUtc is { } consent)
            timeline.Add(("CONSENT ACCEPTED", FormatUtc(consent)));
        if (!string.IsNullOrWhiteSpace(signer.VerificationMethod))
            timeline.Add(("IDENTITY VERIFIED", signer.VerificationMethod!));
        if (signer.SignedAtUtc is { } signed)
            timeline.Add((signer.Status.ToString().ToUpperInvariant(), FormatUtc(signed)));
        else
            timeline.Add(("STATUS", signer.Status.ToString()));

        var meta = new List<(string Label, string Value)>();
        if (!string.IsNullOrWhiteSpace(signer.VerificationMethod))
            meta.Add(("Verification", signer.VerificationMethod!));
        meta.Add(("Client IP", signer.ClientIp ?? "—"));
        if (!string.IsNullOrWhiteSpace(signer.CaptureMethod))
            meta.Add(("Capture", signer.CaptureMethod!));

        var innerWidth = ctx.ContentWidth - 32; // padX * 2
        var uaLines = string.IsNullOrEmpty(signer.UserAgent)
            ? 0
            : WrapLines(ctx.Gfx, signer.UserAgent, metaFont, innerWidth).Count;
        var metaRows = (int)Math.Ceiling(meta.Count / 2.0);
        const double headerH = 34;
        var bodyH = 10 + timeline.Count * 15 + 8 + metaRows * 15 + (uaLines > 0 ? 6 + 11 + uaLines * 11 : 0) + 10;
        return (timeline, meta, uaLines, metaRows, headerH + bodyH);
    }

    private static void DrawSignerCard(RenderContext ctx, CertificateSignerEntry signer)
    {
        const double padX = 16;
        var labelFont = new XFont(SansFamily, 7.5, XFontStyleEx.Bold);
        var metaFont = new XFont(SansFamily, 8.5, XFontStyleEx.Regular);
        var nameFont = new XFont(SansFamily, 11.5, XFontStyleEx.Bold);
        var emailFont = new XFont(SansFamily, 8.5, XFontStyleEx.Regular);

        const double headerH = 34;
        var innerWidth = ctx.ContentWidth - padX * 2;
        var (timeline, meta, uaLines, metaRows, height) = SignerLayout(ctx, signer);

        ctx.EnsureSpace(height);
        var top = ctx.CursorY;

        // Card border
        ctx.Gfx.DrawRoundedRectangle(
            new XPen(CardBorder, 0.8),
            new XRect(MarginLeft, top, ctx.ContentWidth, height),
            new XSize(12, 12)
        );

        // Header band
        var bandPath = new XRect(MarginLeft, top, ctx.ContentWidth, headerH);
        ctx.Gfx.DrawRoundedRectangle(new XSolidBrush(Band), bandPath, new XSize(12, 12));
        ctx.Gfx.DrawRectangle(new XSolidBrush(Band), new XRect(MarginLeft, top + headerH - 12, ctx.ContentWidth, 12)); // square off bottom of band

        // Order bullet
        var bulletRect = new XRect(MarginLeft + padX, top + 7, 20, 20);
        ctx.Gfx.DrawEllipse(new XSolidBrush(BrandPrimary), bulletRect);
        ctx.Gfx.DrawString(signer.Order.ToString(), labelFont, XBrushes.White, bulletRect, XStringFormats.Center);

        var nameX = MarginLeft + padX + 28;
        ctx.Gfx.DrawString(
            EllipsizeToWidth(ctx.Gfx, signer.FullName, nameFont, ctx.ContentWidth - 180),
            nameFont,
            new XSolidBrush(TextPrimary),
            new XPoint(nameX, top + 16)
        );
        ctx.Gfx.DrawString(
            EllipsizeToWidth(ctx.Gfx, signer.Email, emailFont, ctx.ContentWidth - 180),
            emailFont,
            new XSolidBrush(TextMuted),
            new XPoint(nameX, top + 27)
        );
        DrawStatusPill(ctx, signer.Status, top + 10);

        // Body: timeline
        var tx = MarginLeft + padX + 6; // timeline axis
        var textX = tx + 16;
        var firstDotY = top + headerH + 14;
        var lastDotY = firstDotY + (timeline.Count - 1) * 15;
        ctx.Gfx.DrawLine(new XPen(RuleColor, 1.5), tx, firstDotY, tx, lastDotY);
        var ey = firstDotY;
        foreach (var (label, val) in timeline)
        {
            ctx.Gfx.DrawEllipse(new XSolidBrush(BrandAccent), new XRect(tx - 3.5, ey - 3.5, 7, 7));
            ctx.Gfx.DrawString(label, labelFont, new XSolidBrush(TextPrimary), new XPoint(textX, ey + 3));
            var labelW = ctx.Gfx.MeasureString(label, labelFont).Width;
            ctx.Gfx.DrawString(val, metaFont, new XSolidBrush(TextMuted), new XPoint(textX + labelW + 10, ey + 3));
            ey += 15;
        }

        // Meta grid under a divider
        var gy = lastDotY + 12;
        DrawDashedLine(ctx.Gfx, MarginLeft + padX, gy - 4, ctx.ContentRight - padX, gy - 4);
        var colW = (innerWidth) / 2;
        for (var i = 0; i < meta.Count; i++)
        {
            var col = i % 2;
            var row = i / 2;
            var cx = MarginLeft + padX + col * colW;
            var cy = gy + row * 15;
            ctx.Gfx.DrawString(
                meta[i].Label.ToUpperInvariant(),
                labelFont,
                new XSolidBrush(TextMuted),
                new XPoint(cx, cy + 8)
            );
            var lw = ctx.Gfx.MeasureString(meta[i].Label.ToUpperInvariant(), labelFont).Width;
            ctx.Gfx.DrawString(
                EllipsizeToWidth(ctx.Gfx, meta[i].Value, metaFont, colW - lw - 16),
                metaFont,
                new XSolidBrush(TextPrimary),
                new XPoint(cx + lw + 8, cy + 8)
            );
        }

        if (uaLines > 0)
        {
            var uy = gy + metaRows * 15 + 4;
            ctx.Gfx.DrawString(
                "USER AGENT",
                labelFont,
                new XSolidBrush(TextMuted),
                new XPoint(MarginLeft + padX, uy + 8)
            );
            uy += 11;
            foreach (var line in WrapLines(ctx.Gfx, signer.UserAgent!, metaFont, innerWidth))
            {
                ctx.Gfx.DrawString(line, metaFont, new XSolidBrush(TextPrimary), new XPoint(MarginLeft + padX, uy + 8));
                uy += 11;
            }
        }

        ctx.CursorY = top + height;
    }

    private static void DrawStatusPill(RenderContext ctx, SignerStatus status, double top)
    {
        var badgeFont = new XFont(SansFamily, 7.5, XFontStyleEx.Bold);
        var statusText = status.ToString().ToUpperInvariant();
        var bg = status switch
        {
            SignerStatus.Signed => OkBg,
            SignerStatus.Rejected => RejectBg,
            _ => NeutralBg,
        };
        var ink = status switch
        {
            SignerStatus.Signed => OkInk,
            SignerStatus.Rejected => RejectInk,
            _ => NeutralInk,
        };
        var size = ctx.Gfx.MeasureString(statusText, badgeFont);
        var rect = new XRect(ctx.ContentRight - size.Width - 16 - 14, top, size.Width + 14, 16);
        ctx.Gfx.DrawRoundedRectangle(new XSolidBrush(bg), rect, new XSize(8, 8));
        ctx.Gfx.DrawString(statusText, badgeFont, new XSolidBrush(ink), rect, XStringFormats.Center);
    }

    // ------------------------------------------------------------------
    // Preparer
    // ------------------------------------------------------------------

    private static void WritePreparer(RenderContext ctx, CertificatePreparerEntry? preparer)
    {
        if (preparer is null)
            return;

        WriteSectionHeader(ctx, "Preparer — electronic return originator (ERO)");
        var pairs = new List<(string, string)> { ("Name", preparer.DisplayName) };
        if (!string.IsNullOrEmpty(preparer.MaskedIdentifier))
            pairs.Add(("PTIN / EFIN", preparer.MaskedIdentifier));
        pairs.Add(("Signed (UTC)", preparer.SignedAtUtc is { } at ? FormatUtc(at) : "—"));
        DrawTwoColumnGrid(ctx, pairs.ToArray());
        ctx.CursorY += 8;
    }

    // ------------------------------------------------------------------
    // Consent
    // ------------------------------------------------------------------

    private static void WriteConsent(RenderContext ctx)
    {
        WriteSectionHeader(ctx, "Electronic records & signature consent");
        var font = new XFont(SansFamily, 9, XFontStyleEx.Regular);
        var text =
            "Every signer was presented with the Electronic Record and Signature Disclosure and agreed to conduct this "
            + "transaction electronically before signing, as required by the U.S. ESIGN Act and the Uniform Electronic "
            + "Transactions Act (UETA). Each signer's consent timestamp is recorded in their audit trail above.";

        // Green check bullet + wrapped text indented past it.
        ctx.EnsureSpace(WrapLines(ctx.Gfx, text, font, ctx.ContentWidth - 20).Count * 13 + 6);
        var checkTop = ctx.CursorY;
        ctx.Gfx.DrawEllipse(new XSolidBrush(OkBg), new XRect(MarginLeft, checkTop + 1, 13, 13));
        DrawCheck(ctx.Gfx, MarginLeft + 6.5, checkTop + 7.8, 7.5, OkInk, 1.4);
        foreach (var line in WrapLines(ctx.Gfx, text, font, ctx.ContentWidth - 20))
        {
            ctx.Gfx.DrawString(line, font, new XSolidBrush(TextPrimary), new XPoint(MarginLeft + 20, ctx.CursorY + 10));
            ctx.CursorY += 13;
        }
        ctx.CursorY += 10;
    }

    // ------------------------------------------------------------------
    // Digital seal panel
    // ------------------------------------------------------------------

    private static void WriteSeal(RenderContext ctx, CertificateSealInfo? seal)
    {
        if (seal is null)
            return;

        WriteSectionHeader(ctx, "Digital seal");

        const double padX = 16;
        var labelFont = new XFont(SansFamily, 8.5, XFontStyleEx.Bold);
        var valueFont = new XFont(SansFamily, 8.5, XFontStyleEx.Regular);
        var monoFont = new XFont(MonoFamily, 8, XFontStyleEx.Regular);
        var noteFont = new XFont(SansFamily, 8.5, XFontStyleEx.Regular);

        var innerWidth = ctx.ContentWidth - padX * 2;
        var note =
            "Every document in this envelope carries this PAdES digital signature. Open a sealed PDF in Adobe Acrobat → Signature Panel to validate it.";
        var noteLines = WrapLines(ctx.Gfx, note, noteFont, innerWidth - 42);

        var rows = new (string Label, string Value, bool Mono)[]
        {
            ("Signer (subject)", seal.Subject, true),
            ("Issuer", seal.Issuer, true),
            ("Serial", seal.SerialNumber, true),
            ("Algorithm", seal.SignatureAlgorithm, false),
            ("Valid until (UTC)", FormatUtc(seal.NotAfterUtc), false),
            ("Level", "PAdES-B", false),
        };

        var headerBlock = 12 + Math.Max(32, noteLines.Count * 11) + 10;
        var height = headerBlock + rows.Length * 15 + 12;

        ctx.EnsureSpace(height);
        var top = ctx.CursorY;
        ctx.Gfx.DrawRoundedRectangle(
            new XSolidBrush(SealBg),
            new XRect(MarginLeft, top, ctx.ContentWidth, height),
            new XSize(12, 12)
        );
        ctx.Gfx.DrawRoundedRectangle(
            new XPen(SealBorder, 1),
            new XRect(MarginLeft, top, ctx.ContentWidth, height),
            new XSize(12, 12)
        );

        // Seal badge (ring + check)
        var badgeCx = MarginLeft + padX + 14;
        var badgeCy = top + 14 + 12;
        ctx.Gfx.DrawEllipse(new XPen(BrandAccent, 1.6), new XRect(badgeCx - 13, badgeCy - 13, 26, 26));
        ctx.Gfx.DrawEllipse(new XSolidBrush(AccentTint), new XRect(badgeCx - 9, badgeCy - 9, 18, 18));
        DrawCheck(ctx.Gfx, badgeCx, badgeCy, 13, BrandAccent, 2.2);

        var noteX = badgeCx + 24;
        var ny = top + 14;
        foreach (var line in noteLines)
        {
            ctx.Gfx.DrawString(line, noteFont, new XSolidBrush(TextMuted), new XPoint(noteX, ny + 9));
            ny += 11;
        }

        var y = top + headerBlock;
        DrawDashedLine(ctx.Gfx, MarginLeft + padX, y - 4, ctx.ContentRight - padX, y - 4);
        var valueX = MarginLeft + padX + 128;
        foreach (var (label, value, mono) in rows)
        {
            ctx.Gfx.DrawString(label, labelFont, new XSolidBrush(TextMuted), new XPoint(MarginLeft + padX, y + 9));
            var font = mono ? monoFont : valueFont;
            ctx.Gfx.DrawString(
                EllipsizeToWidth(ctx.Gfx, value, font, ctx.ContentRight - padX - valueX),
                font,
                new XSolidBrush(TextPrimary),
                new XPoint(valueX, y + 9)
            );
            y += 15;
        }

        ctx.CursorY = top + height + 10;
    }

    // ------------------------------------------------------------------
    // Legal footer
    // ------------------------------------------------------------------

    private static void WriteLegalFooter(RenderContext ctx, CertificateOfCompletionModel model)
    {
        var boldFont = new XFont(SansFamily, 7.5, XFontStyleEx.Bold);
        var italicFont = new XFont(SansFamily, 7.5, XFontStyleEx.Italic);

        ctx.EnsureSpace(58);
        ctx.CursorY += 6;
        ctx.Gfx.DrawLine(new XPen(RuleColor, 0.5), MarginLeft, ctx.CursorY, ctx.ContentRight, ctx.CursorY);
        ctx.CursorY += 12;

        ctx.Gfx.DrawString(
            $"Generated by {BrandName}",
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
            "This certificate is an integral part of the signed documents and constitutes evidence of the signature "
            + "process under the U.S. ESIGN Act (15 U.S.C. §7001) and the Uniform Electronic Transactions Act (UETA); "
            + "for tax e-file authorizations (Form 8879) it follows IRS Pub. 1345, and for signers in the EU it is "
            + "issued in accordance with Regulation (EU) 910/2014 (eIDAS). IP addresses and timestamps reflect the "
            + "connection and system data captured at each event and are indicative, not a precise physical location.";
        WriteWrapped(ctx, disclaimer, italicFont, TextMuted, MarginLeft, ctx.ContentWidth, 11);

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
    // Primitives
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

    /// <summary>Rejilla de pares etiqueta/valor en 2 columnas (resumen, preparador).</summary>
    private static void DrawTwoColumnGrid(RenderContext ctx, (string Label, string Value)[] pairs)
    {
        var labelFont = new XFont(SansFamily, 8.5, XFontStyleEx.Bold);
        var valueFont = new XFont(SansFamily, 9, XFontStyleEx.Regular);
        var colW = ctx.ContentWidth / 2;
        var rows = (int)Math.Ceiling(pairs.Length / 2.0);
        ctx.EnsureSpace(rows * 16 + 4);
        var top = ctx.CursorY;
        for (var i = 0; i < pairs.Length; i++)
        {
            var col = i % 2;
            var row = i / 2;
            var cx = MarginLeft + col * colW;
            var cy = top + row * 16;
            ctx.Gfx.DrawString(pairs[i].Label, labelFont, new XSolidBrush(TextMuted), new XPoint(cx, cy + 9));
            var lw = Math.Max(ctx.Gfx.MeasureString(pairs[i].Label, labelFont).Width + 10, 118);
            ctx.Gfx.DrawString(
                EllipsizeToWidth(ctx.Gfx, pairs[i].Value, valueFont, colW - lw - 12),
                valueFont,
                new XSolidBrush(TextPrimary),
                new XPoint(cx + lw, cy + 9)
            );
        }
        ctx.CursorY = top + rows * 16;
    }

    private static void DrawInlineKv(RenderContext ctx, string label, string value, double x, double y, bool monoValue)
    {
        var labelFont = new XFont(SansFamily, 8.5, XFontStyleEx.Bold);
        var valueFont = monoValue
            ? new XFont(MonoFamily, 8, XFontStyleEx.Regular)
            : new XFont(SansFamily, 8.5, XFontStyleEx.Regular);
        ctx.Gfx.DrawString(label, labelFont, new XSolidBrush(TextMuted), new XPoint(x, y + 9));
        var lw = Math.Max(ctx.Gfx.MeasureString(label, labelFont).Width + 10, 128);
        ctx.Gfx.DrawString(value, valueFont, new XSolidBrush(TextPrimary), new XPoint(x + lw, y + 9));
    }

    private static void DrawDashedLine(XGraphics gfx, double x1, double y1, double x2, double y2)
    {
        var pen = new XPen(RuleColor, 0.6) { DashStyle = XDashStyle.Dash };
        gfx.DrawLine(pen, x1, y1, x2, y2);
    }

    /// <summary>Dibuja un checkmark con dos trazos (en vez del glifo ✓, que no todas las fuentes traen).</summary>
    private static void DrawCheck(XGraphics gfx, double cx, double cy, double size, XColor color, double thickness)
    {
        var pen = new XPen(color, thickness) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
        var p1 = new XPoint(cx - size * 0.42, cy + size * 0.02);
        var p2 = new XPoint(cx - size * 0.10, cy + size * 0.34);
        var p3 = new XPoint(cx + size * 0.44, cy - size * 0.36);
        gfx.DrawLines(pen, [p1, p2, p3]);
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
        width = Math.Min(width, 180);
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
            return null;
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
            return null;
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
