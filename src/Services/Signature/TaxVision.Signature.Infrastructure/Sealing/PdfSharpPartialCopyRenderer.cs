using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using TaxVision.Signature.Application.Abstractions.Delivery;

namespace TaxVision.Signature.Infrastructure.Sealing;

/// <summary>
/// F7 — rendea el PDF decorado que recibe un firmante al firmar. Reusa el estampado del sealing
/// engine (StampAllFields) pero añade un banner superior informativo y pie con progreso. NO sella
/// PAdES, NO toca TSA, NO guarda hash final en el aggregate.
/// Estilo DocuSign/Adobe Sign: nada de watermark diagonal que tape el contenido — una barra
/// discreta arriba que explica el estado + el footer con detalles.
/// </summary>
public sealed class PdfSharpPartialCopyRenderer : IPartialCopyRenderer
{
    public PartialCopyResult Render(PartialCopyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var input = new MemoryStream(request.OriginalPdfBytes, writable: false);
        using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        PdfSharpSealingEngine.StampAllFields(pdf, request.SignedFields);
        DrawAdvanceCopyBanner(pdf, request);
        DrawProgressFooter(pdf, request);

        using var output = new MemoryStream();
        pdf.Save(output, closeStream: false);
        var bytes = output.ToArray();
        return new PartialCopyResult(bytes, PdfSharpSealingEngine.ComputeSha256(bytes));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void DrawAdvanceCopyBanner(PdfDocument pdf, PartialCopyRequest request)
    {
        // Barra fina azul claro arriba de cada página, estilo DocuSign/Adobe Sign.
        var background = new XSolidBrush(XColor.FromArgb(232, 240, 250));
        var titleBrush = new XSolidBrush(XColor.FromArgb(20, 45, 90));
        var textBrush = new XSolidBrush(XColor.FromArgb(60, 80, 110));
        var titleFont = new XFont("Helvetica", 7.5, XFontStyleEx.Bold);
        var textFont = new XFont("Helvetica", 7.5, XFontStyleEx.Regular);

        const string title = "ADVANCE COPY";
        var pending = request.SignerProgress.Count(s => s.State == PartialCopySignerState.Pending);
        // El mensaje solo promete el PDF sellado cuando el preparador lo va a enviar; si no, es la
        // constancia de la firma del firmante y nada más.
        var message = (pending, request.SendSealedToSigners) switch
        {
            (> 0, true) =>
                $"Signed copy for {request.RecipientSignerDisplayName}. Final sealed PDF will be issued once the remaining {pending} signer{(pending == 1 ? "" : "s")} finish.",
            (> 0, false) =>
                $"Signed copy for {request.RecipientSignerDisplayName}. Keep it as a record of your signature while the remaining {pending} signer{(pending == 1 ? "" : "s")} finish.",
            (0, true) =>
                $"Signed copy for {request.RecipientSignerDisplayName}. Final sealed PDF with legal evidence follows shortly.",
            _ => $"Signed copy for {request.RecipientSignerDisplayName}. Keep it as a record of your signature.",
        };

        for (var i = 0; i < pdf.PageCount; i++)
        {
            var page = pdf.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var pageWidth = page.Width.Point;

            // Banner: 16pt alto, pegado al borde superior con un pequeño margen lateral.
            gfx.DrawRectangle(background, 18, 10, pageWidth - 36, 16);
            gfx.DrawString(title, titleFont, titleBrush, new XPoint(26, 21));
            var titleSize = gfx.MeasureString(title, titleFont);
            gfx.DrawString(" · " + message, textFont, textBrush, new XPoint(26 + titleSize.Width, 21));
        }
    }

    private static void DrawProgressFooter(PdfDocument pdf, PartialCopyRequest request)
    {
        var labelFont = new XFont("Helvetica", 6, XFontStyleEx.Bold);
        var textFont = new XFont("Helvetica", 6, XFontStyleEx.Regular);
        var mutedBrush = new XSolidBrush(XColor.FromArgb(96, 105, 120));
        var accentBrush = new XSolidBrush(XColor.FromArgb(20, 45, 90));
        var rulePen = new XPen(XColor.FromArgb(210, 215, 225), 0.4);

        var signedCount = request.SignerProgress.Count(s => s.State == PartialCopySignerState.Signed);
        var pendingNames = request
            .SignerProgress.Where(s => s.State == PartialCopySignerState.Pending)
            .Select(s => s.DisplayName)
            .ToList();
        var progressLine =
            $"Signatures collected {signedCount}/{request.SignerProgress.Count}"
            + (pendingNames.Count > 0 ? $" · Pending: {string.Join(", ", pendingNames)}" : string.Empty);

        var recipientLine =
            $"Delivered to {request.RecipientSignerDisplayName} on "
            + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        for (var i = 0; i < pdf.PageCount; i++)
        {
            var page = pdf.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var pageWidth = page.Width.Point;
            var pageHeight = page.Height.Point;
            var y = pageHeight - 22;

            gfx.DrawLine(rulePen, 24, y - 4, pageWidth - 24, y - 4);
            gfx.DrawString("In-progress copy", labelFont, accentBrush, new XPoint(24, y + 2));
            gfx.DrawString($" • {progressLine}", textFont, mutedBrush, new XPoint(24 + 72, y + 2));

            var pageLabel = $"Page {i + 1} / {pdf.PageCount}";
            var pageSize = gfx.MeasureString(pageLabel, textFont);
            gfx.DrawString(pageLabel, textFont, mutedBrush, new XPoint(pageWidth - 24 - pageSize.Width, y + 2));

            gfx.DrawString(recipientLine, textFont, mutedBrush, new XPoint(24, y + 11));
        }
    }
}
