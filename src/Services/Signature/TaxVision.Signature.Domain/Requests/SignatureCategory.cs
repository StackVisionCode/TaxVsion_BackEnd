namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Categoría legal del documento a firmar. Determina el texto de consentimiento por
/// defecto, los canales de verificación recomendados y la política de retención.
/// </summary>
public enum SignatureCategory
{
    /// <summary>Documentos fiscales (Form 8879, autorizaciones IRS, etc.).</summary>
    Fiscal,

    /// <summary>Cartas de compromiso entre la oficina y el cliente.</summary>
    EngagementLetter,

    /// <summary>Consentimiento IRC §7216 para divulgar información de la declaración.</summary>
    ConsentToDisclose,

    /// <summary>Autorizaciones bancarias (deposito directo, débitos, ACH).</summary>
    BankAuth,

    /// <summary>Otras categorías definidas por el tenant.</summary>
    Other,
}
