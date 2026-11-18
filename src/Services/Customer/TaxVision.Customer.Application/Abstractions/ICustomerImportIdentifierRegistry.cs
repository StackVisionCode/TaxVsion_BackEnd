using TaxVision.Customer.Application.Imports.Dtos;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Identificadores únicos por oficina que ya tienen dueño, para fallar la fila antes de guardar en vez de romper el
/// bloque entero con el índice único. Llaves: blind index actual del SSN/EIN y email normalizado. El SSN de cónyuge
/// apunta a la relación que lo tiene, para que un Overwrite no choque con su propio cónyuge.
/// </summary>
public sealed record ImportTakenIdentifiers(
    IReadOnlyDictionary<string, Guid> TaxIdOwners,
    IReadOnlyDictionary<string, Guid> SpouseTaxIdRelations,
    IReadOnlyDictionary<string, Guid> EmailOwners
)
{
    public static readonly ImportTakenIdentifiers None = new(
        new Dictionary<string, Guid>(),
        new Dictionary<string, Guid>(),
        new Dictionary<string, Guid>()
    );
}

public interface ICustomerImportIdentifierRegistry
{
    /// <summary>
    /// Una sola consulta por bloque. SSN/EIN incluye archivados (su índice único también); el email no.
    /// </summary>
    Task<ImportTakenIdentifiers> FindTakenAsync(
        Guid tenantId,
        IReadOnlyList<ImportCustomerRow> chunk,
        CancellationToken ct
    );
}
