using BuildingBlocks.Results;

namespace TaxVision.Connectors.Application.OAuth;

/// <summary>
/// Devuelve siempre un access token válido para una TenantEmailAccount, refrescándolo
/// proactivamente si ya expiró o expira en menos de 10 minutos. Los clients de Fase 5
/// (Gmail/Graph) llaman a esto antes de cada request a la API del proveedor — nunca leen
/// el OAuthToken directamente.
/// </summary>
public interface IOAuthTokenManager
{
    Task<Result<string>> GetValidAccessTokenAsync(Guid accountId, CancellationToken ct = default);
}
