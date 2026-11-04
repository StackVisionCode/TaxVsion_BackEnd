namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Revisión del PDF original en el servidor (formato, tamaño, páginas, firmas previas). Solo un documento
/// <see cref="Verified"/> puede enviarse a firmar.
/// </summary>
public enum DocumentVerificationStatus
{
    /// <summary>El archivo aún no está disponible en CloudStorage (scan en curso) o no se pudo revisar.</summary>
    Pending = 0,
    Verified = 1,

    /// <summary>El archivo no cumple las reglas; hay que reemplazarlo antes de enviar.</summary>
    Rejected = 2,
}
