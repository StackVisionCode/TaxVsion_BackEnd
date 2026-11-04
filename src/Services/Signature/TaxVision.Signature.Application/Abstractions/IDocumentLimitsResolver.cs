using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>Límites de aceptación de documentos vigentes para un tenant (tamaño y páginas del PDF a firmar).</summary>
public interface IDocumentLimitsResolver
{
    Task<DocumentLimits> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
