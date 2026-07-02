using TaxVision.Customer.Application.Imports.Dtos;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Detecta duplicados en batch para un chunk del archivo de import.
/// Usa blind index (HMAC-SHA256 por tenant) para SSN/EIN, sin descifrar nada.
/// Tambien matchea por email normalizado, telefono E164 y (nombre normalizado + DOB).
/// </summary>
public interface ICustomerDuplicateDetector
{
    /// <summary>
    /// Para cada fila del chunk que coincida con un customer existente, devuelve un DuplicateMatch.
    /// Las filas sin match no aparecen en el resultado.
    /// UNA sola query por chunk, no por fila.
    /// </summary>
    Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
        Guid tenantId,
        IReadOnlyList<ImportCustomerRow> chunk,
        CancellationToken ct
    );
}
