namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Tipo de campo colocado sobre el documento por el preparador y rellenado por el firmante
/// durante el flujo de firma.
/// </summary>
public enum SignatureFieldKind
{
    /// <summary>Firma manuscrita capturada (dibujada, escrita o subida).</summary>
    Signature,

    /// <summary>Iniciales del firmante.</summary>
    Initials,

    /// <summary>Fecha autogenerada al firmar (no editable por el firmante).</summary>
    Date,

    /// <summary>Campo de texto libre requerido o opcional.</summary>
    Text,

    /// <summary>Casilla de verificación (checkbox obligatorio u opcional).</summary>
    Checkbox,
}
