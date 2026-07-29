using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>
/// Descarga el documento legal publicado en ContentUri y calcula su hash — PublishTermsVersionHandler
/// nunca confia en un ContentHash provisto por el llamador, siempre lo recalcula el propio backend
/// a partir del contenido real, para que el hash sea una garantia verificable y no un dato de
/// entrada que alguien pudo copiar mal (o directamente inventar).
/// </summary>
public interface ITermsDocumentHasher
{
    Task<Result<string>> ComputeHashAsync(string contentUri, CancellationToken ct = default);
}
