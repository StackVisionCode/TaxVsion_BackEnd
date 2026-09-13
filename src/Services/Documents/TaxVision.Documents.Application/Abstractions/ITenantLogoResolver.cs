namespace TaxVision.Documents.Application.Abstractions;

/// <summary>
/// Resuelve el logo de la marca del tenant como un <c>data:</c> URI listo para embeber en el PDF de la
/// factura. Combina la proyección local (fileId) + la bajada de bytes desde CloudStorage. Best-effort:
/// devuelve <c>null</c> si el tenant no tiene logo o si la bajada falla — el PDF sigue sin logo.
/// </summary>
public interface ITenantLogoResolver
{
    Task<string?> ResolveLogoDataUriAsync(Guid tenantId, CancellationToken ct = default);
}
