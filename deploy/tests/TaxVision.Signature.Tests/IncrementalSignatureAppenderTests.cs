using System.Text;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using TaxVision.Signature.Infrastructure.Sealing;
using TaxVision.Signature.Infrastructure.Sealing.Pades;

namespace TaxVision.Signature.Tests;

/// <summary>
/// Regresión del bug que corrompía el documento sellado: el incremental update de firma acuñaba un
/// Catalog nuevo SIN <c>/Pages</c> (obligatorio, PDF 32000-1 §7.7.2), y todo parser conforme rechazaba
/// el PDF. El fix REDEFINE el Catalog original conservando sus claves. Estos tests re-parsean la salida
/// firmada — algo que ningún test hacía antes, por eso el bug estuvo latente.
/// </summary>
public class IncrementalSignatureAppenderTests
{
    public IncrementalSignatureAppenderTests()
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new SealingFontResolver();
    }

    private static byte[] NewPdf()
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Original_pdfsharp_catalog_has_pages()
    {
        var trailer = PdfTrailerParser.Parse(NewPdf());

        Assert.True(trailer.IsSuccess);
        Assert.Contains("/Pages", trailer.Value.RootCatalogKeys);
    }

    [Fact]
    public void Signed_output_preserves_root_and_keeps_pages_and_adds_acroform()
    {
        var pdf = NewPdf();
        var trailer = PdfTrailerParser.Parse(pdf);
        Assert.True(trailer.IsSuccess);

        var appender = new IncrementalSignatureAppender(new PadesOptions());
        var layout = appender.Append(pdf, trailer.Value);
        var signed = layout.PdfWithPlaceholders;

        // El PDF firmado debe re-parsear como un incremental update válido.
        var reparsed = PdfTrailerParser.Parse(signed);
        Assert.True(reparsed.IsSuccess);

        // Mismo /Root que el original (se REDEFINIÓ, no se acuñó uno nuevo).
        Assert.Equal(trailer.Value.RootObjectNumber, reparsed.Value.RootObjectNumber);

        // El Catalog redefinido conserva /Pages — la clave que el bug perdía y que corrompía el PDF.
        Assert.Contains("/Pages", reparsed.Value.RootCatalogKeys);

        // En los bytes crudos: el Catalog ganó /AcroForm, la firma está presente y la cadena /Prev
        // apunta al xref original. (PdfTrailerParser hace StripAcroForm al leer, por eso se verifica
        // sobre el texto y no sobre RootCatalogKeys.)
        var text = Encoding.Latin1.GetString(signed);
        Assert.Contains("/AcroForm", text);
        Assert.Contains("/ByteRange", text);
        Assert.Contains($"/Prev {trailer.Value.StartXref}", text);
    }

    [Fact]
    public void Signed_output_starts_with_pdf_header_and_ends_with_eof()
    {
        var pdf = NewPdf();
        var trailer = PdfTrailerParser.Parse(pdf);
        var layout = new IncrementalSignatureAppender(new PadesOptions()).Append(pdf, trailer.Value);
        var text = Encoding.Latin1.GetString(layout.PdfWithPlaceholders);

        Assert.StartsWith("%PDF", text);
        Assert.EndsWith("%%EOF\n", text);
    }
}
