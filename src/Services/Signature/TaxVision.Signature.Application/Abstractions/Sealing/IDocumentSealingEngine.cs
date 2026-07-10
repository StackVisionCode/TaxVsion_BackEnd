namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>
/// Motor de sellado: recibe el PDF original + los campos ya validados y devuelve el
/// PDF con las firmas estampadas. Puro, sin efectos secundarios: no accede a red ni a
/// almacenamiento. Puede reemplazarse con distintas implementaciones (PdfSharp por
/// defecto, ExternalPdfSigner en el futuro, etc.).
/// </summary>
public interface IDocumentSealingEngine
{
    SealingResult Seal(SealingRequest request);
}
