using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Application.Abstractions;

/// <summary>
/// Resuelve el logo de la marca del tenant como un <c>data:</c> URI listo para embeber en el PDF.
/// Combina la proyección local (fileId) + la bajada de bytes desde CloudStorage. <paramref name="audience"/>
/// elige la superficie: Staff = CRM; Client = Portal y, si no hay o no baja, CRM. Best-effort: devuelve
/// <c>null</c> si el tenant no tiene logo o si la bajada falla — el PDF sigue sin logo.
/// </summary>
public interface ITenantLogoResolver
{
    Task<string?> ResolveLogoDataUriAsync(Guid tenantId, LogoAudience audience, CancellationToken ct = default);
}
