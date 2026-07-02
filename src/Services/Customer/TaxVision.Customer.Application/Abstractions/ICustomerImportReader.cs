using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Abstraccion para parsear archivos de import en modo stream. Una implementacion por formato (CSV, XLSX).
/// El handler del worker la usa via factory: el SourceKind del attempt determina cual instancia se inyecta.
/// </summary>
public interface ICustomerImportReader
{
    ImportSourceKind SourceKind { get; }

    /// <summary>
    /// Lee filas del stream una por una. Implementaciones DEBEN respetar el cancellation token
    /// y NO cargar el archivo completo en memoria.
    /// </summary>
    IAsyncEnumerable<ImportCustomerRow> ReadAsync(Stream stream, CancellationToken ct);
}
