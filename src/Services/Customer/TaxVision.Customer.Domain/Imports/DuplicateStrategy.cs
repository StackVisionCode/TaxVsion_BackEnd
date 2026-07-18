namespace TaxVision.Customer.Domain.Imports;

/// <summary>
/// Politica del operador cuando el detector encuentra un cliente existente que coincide con una fila del archivo.
/// </summary>
public enum DuplicateStrategy
{
    /// <summary>Mantener el existente sin tocarlo y reportar la fila como Skipped.</summary>
    Skip,

    /// <summary>Completar campos vacios del existente con valores nuevos; nunca pisar lo que ya hay.</summary>
    Merge,

    /// <summary>Reemplazar completamente el existente con los datos del archivo. Requiere autorizacion explicita.</summary>
    Overwrite,
}
