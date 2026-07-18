namespace TaxVision.Signature.Domain.Validation;

/// <summary>Veredicto del preflight de un documento antes de convertirse en solicitud de firma.</summary>
public enum DocumentValidationVerdict
{
    /// <summary>El documento es firmable — pasa MIME, tamaño, integridad y no tiene firmas previas.</summary>
    Accepted,

    /// <summary>Rechazado por regla técnica (MIME, tamaño, páginas, PDF corrupto, firmas previas).</summary>
    Rejected,
}
