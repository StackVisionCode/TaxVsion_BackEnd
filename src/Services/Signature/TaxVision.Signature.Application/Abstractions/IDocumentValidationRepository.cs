using TaxVision.Signature.Domain.Validation;

namespace TaxVision.Signature.Application.Abstractions;

public interface IDocumentValidationRepository
{
    Task AddAsync(DocumentValidationRecord record, CancellationToken ct = default);

    /// <summary>
    /// Último registro de este contenido cuyo PDF se pudo abrir (trae el número de páginas). El contenido es
    /// el mismo si el SHA-256 coincide, así que sus hechos sirven sin volver a descargar el archivo.
    /// </summary>
    Task<DocumentValidationRecord?> FindInspectedAsync(
        Guid tenantId,
        string contentSha256,
        CancellationToken ct = default
    );
}
