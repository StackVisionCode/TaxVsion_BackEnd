using System.Security.Cryptography;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using TaxVision.Signature.Application.Abstractions.Sealing;

namespace TaxVision.Signature.Infrastructure.Sealing;

/// <summary>
/// Certificate of Completion en A4: encabezado con título, metadata general del proceso
/// (tenant, request, hashes pre/post, categoría, timestamps) y tabla de firmantes con
/// nombre, email, orden, status, IP y user agent. Sin dependencias externas.
/// </summary>
public sealed class PdfSharpCertificateRenderer : ICertificateOfCompletionRenderer
{
    private const double MarginLeft = 40;
    private const double MarginRight = 40;
    private const double MarginTop = 40;
    private const double LineHeight = 14;

    public CertificateResult Render(CertificateOfCompletionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        using var pdf = new PdfDocument();
        pdf.Info.Title = "TaxVision — Certificate of Completion";
        pdf.Info.Author = "TaxVision Signature Service";

        var page = pdf.AddPage();
        page.Size = PageSize.A4;

        using var gfx = XGraphics.FromPdfPage(page);
        var cursorY = MarginTop;
        cursorY = WriteHeader(gfx, page, cursorY);
        cursorY = WriteMetadata(gfx, page, model, cursorY);
        cursorY = WriteSignersTable(gfx, page, model.Signers, cursorY);
        WriteFooter(gfx, page, model);

        using var output = new MemoryStream();
        pdf.Save(output, closeStream: false);
        var bytes = output.ToArray();
        return new CertificateResult(bytes, ComputeSha256(bytes));
    }

    // ------------------------------------------------------------------
    // Métodos privados: uno por sección del layout
    // ------------------------------------------------------------------

    private static double WriteHeader(XGraphics gfx, PdfPage page, double cursorY)
    {
        var titleFont = new XFont("Helvetica", 18, XFontStyleEx.Bold);
        var subtitleFont = new XFont("Helvetica", 10, XFontStyleEx.Regular);
        var brush = new XSolidBrush(XColors.Navy);
        var subBrush = new XSolidBrush(XColors.DimGray);

        gfx.DrawString("Certificate of Completion", titleFont, brush, new XPoint(MarginLeft, cursorY));
        gfx.DrawString(
            "Audit trail for the electronic signature process",
            subtitleFont,
            subBrush,
            new XPoint(MarginLeft, cursorY + 22)
        );

        var rulePen = new XPen(XColors.Navy, 0.75);
        gfx.DrawLine(rulePen, MarginLeft, cursorY + 34, page.Width.Point - MarginRight, cursorY + 34);

        return cursorY + 48;
    }

    private static double WriteMetadata(XGraphics gfx, PdfPage page, CertificateOfCompletionModel model, double cursorY)
    {
        var labelFont = new XFont("Helvetica", 9, XFontStyleEx.Bold);
        var valueFont = new XFont("Helvetica", 9, XFontStyleEx.Regular);
        var brush = XBrushes.Black;

        var rows = new (string Label, string Value)[]
        {
            ("Signature Request Id", model.SignatureRequestId.ToString("D")),
            ("Tenant Id", model.TenantId.ToString("D")),
            ("Document Title", model.Title),
            ("Category", model.Category.ToString()),
            ("Created (UTC)", model.CreatedAtUtc.ToString("O")),
            ("Completed (UTC)", model.CompletedAtUtc.ToString("O")),
            ("Original SHA-256", model.DocumentHashPre),
            ("Sealed SHA-256", model.DocumentHashPost),
        };

        foreach (var row in rows)
        {
            gfx.DrawString(row.Label, labelFont, brush, new XPoint(MarginLeft, cursorY));
            gfx.DrawString(row.Value, valueFont, brush, new XPoint(MarginLeft + 130, cursorY));
            cursorY += LineHeight;
        }

        return cursorY + 12;
    }

    private static double WriteSignersTable(
        XGraphics gfx,
        PdfPage page,
        IReadOnlyList<CertificateSignerEntry> signers,
        double cursorY
    )
    {
        var titleFont = new XFont("Helvetica", 11, XFontStyleEx.Bold);
        gfx.DrawString("Signers", titleFont, XBrushes.Black, new XPoint(MarginLeft, cursorY));
        cursorY += 18;

        var headerFont = new XFont("Helvetica", 8, XFontStyleEx.Bold);
        var cellFont = new XFont("Helvetica", 8, XFontStyleEx.Regular);
        var headerBg = new XSolidBrush(XColor.FromArgb(240, 240, 240));
        var contentRight = page.Width.Point - MarginRight;

        gfx.DrawRectangle(headerBg, MarginLeft, cursorY - 10, contentRight - MarginLeft, 14);
        gfx.DrawString("#", headerFont, XBrushes.Black, new XPoint(MarginLeft + 4, cursorY));
        gfx.DrawString("Name", headerFont, XBrushes.Black, new XPoint(MarginLeft + 24, cursorY));
        gfx.DrawString("Email", headerFont, XBrushes.Black, new XPoint(MarginLeft + 150, cursorY));
        gfx.DrawString("Status", headerFont, XBrushes.Black, new XPoint(MarginLeft + 300, cursorY));
        gfx.DrawString("Signed (UTC)", headerFont, XBrushes.Black, new XPoint(MarginLeft + 360, cursorY));
        gfx.DrawString("Client IP", headerFont, XBrushes.Black, new XPoint(MarginLeft + 460, cursorY));
        cursorY += 12;

        foreach (var signer in signers)
        {
            var signedAt = signer.SignedAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? "—";
            gfx.DrawString(signer.Order.ToString(), cellFont, XBrushes.Black, new XPoint(MarginLeft + 4, cursorY));
            gfx.DrawString(TrimTo(signer.FullName, 24), cellFont, XBrushes.Black, new XPoint(MarginLeft + 24, cursorY));
            gfx.DrawString(TrimTo(signer.Email, 32), cellFont, XBrushes.Black, new XPoint(MarginLeft + 150, cursorY));
            gfx.DrawString(signer.Status.ToString(), cellFont, XBrushes.Black, new XPoint(MarginLeft + 300, cursorY));
            gfx.DrawString(signedAt, cellFont, XBrushes.Black, new XPoint(MarginLeft + 360, cursorY));
            gfx.DrawString(signer.ClientIp ?? "—", cellFont, XBrushes.Black, new XPoint(MarginLeft + 460, cursorY));
            cursorY += 12;
        }

        return cursorY + 8;
    }

    private static void WriteFooter(XGraphics gfx, PdfPage page, CertificateOfCompletionModel model)
    {
        var footerFont = new XFont("Helvetica", 7, XFontStyleEx.Italic);
        var footerBrush = new XSolidBrush(XColors.DimGray);
        var footerText =
            $"Generated by TaxVision Signature Service • Request {model.SignatureRequestId:D} • "
            + $"Rendered {DateTime.UtcNow:O}";
        gfx.DrawString(footerText, footerFont, footerBrush, new XPoint(MarginLeft, page.Height.Point - 24));
    }

    private static string TrimTo(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length > max ? value[..max] + "…" : value);

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
