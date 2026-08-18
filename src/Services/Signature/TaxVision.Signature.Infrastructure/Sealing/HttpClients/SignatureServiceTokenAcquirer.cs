using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Signature.Infrastructure.Sealing.HttpClients;

/// <summary>
/// Adquisición y caché de tokens M2M (grant client-credentials) por tenant. Alimenta
/// al <see cref="SignatureCloudStorageClient"/> cuando el worker background necesita
/// hablar con CloudStorage sin request de usuario.
/// </summary>
internal interface ISignatureServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}

// RateLimit Fase 2 — implementa también el contrato compartido de BuildingBlocks
// (BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer) directamente sobre esta misma
// clase, para que HttpPlanRateLimitReader pueda reusar el acquirer M2M ya existente de Signature
// (F25) sin duplicar lógica de adquisición/caché de tokens. El forwarding de DI vive en
// DependencyInjection.cs (AddRateLimitTierQuotas).
internal sealed class SignatureServiceTokenAcquirer(
    HttpClient http,
    IOptions<ServiceAuthClientOptions> options,
    ILogger<SignatureServiceTokenAcquirer> logger
) : ISignatureServiceTokenAcquirer, BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromSeconds(30);

    private static readonly ExpiringValueCache<Guid, string> _cache = new(RefreshBuffer);

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId) || string.IsNullOrWhiteSpace(opt.ClientSecret))
        {
            logger.LogWarning("Signature:ServiceAuth is not configured; cannot acquire a service token.");
            return null;
        }

        try
        {
            return await _cache.GetOrCreateAsync(
                tenantId,
                async innerCt =>
                {
                    var grant = await http.RequestServiceTokenAsync(opt.ClientId, opt.ClientSecret, tenantId, innerCt);
                    return (grant.AccessToken, grant.ExpiresAtUtc);
                },
                ct
            );
        }
        catch (ServiceTokenAcquisitionException ex)
        {
            logger.LogWarning(ex, "Could not acquire a service token for tenant {TenantId}.", tenantId);
            return null;
        }
    }
}
