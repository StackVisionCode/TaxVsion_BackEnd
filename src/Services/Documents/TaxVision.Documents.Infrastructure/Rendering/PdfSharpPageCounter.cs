using PdfSharp.Pdf.IO;
using TaxVision.Documents.Application.Abstractions;

namespace TaxVision.Documents.Infrastructure.Rendering;

/// <summary>
/// Cuenta páginas abriendo el PDF en modo Import (solo parsea la estructura, no renderiza texto), así
/// que no necesita el font resolver de PdfSharp. P-18: ubicar la caja de firma en la última página.
/// </summary>
public sealed class PdfSharpPageCounter : IPdfPageCounter
{
    public int CountPages(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }
}
