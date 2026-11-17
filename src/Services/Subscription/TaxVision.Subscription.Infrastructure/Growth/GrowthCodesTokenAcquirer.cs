using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.Subscription.Infrastructure.Growth;

/// <summary>
/// Credenciales del cliente <c>subscription-growth</c>: Auth le da audiencia <c>taxvision-growth</c> y los scopes de
/// códigos, que exige <c>internal/codes</c>. Distinto del cliente general de Subscription (audiencia amplia).
/// </summary>
public sealed class GrowthCodesAuthOptions
{
    public const string SectionName = "Subscription:GrowthAuth";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>Token M2M por oficina para el canje de códigos en Growth.</summary>
internal interface IGrowthCodesTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}

internal sealed class GrowthCodesTokenAcquirer(
    HttpClient http,
    IOptions<GrowthCodesAuthOptions> options,
    ILogger<GrowthCodesTokenAcquirer> logger
) : IGrowthCodesTokenAcquirer
{
    private static readonly ExpiringValueCache<Guid, string> _cache = new(TimeSpan.FromSeconds(30));

    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var opt = options.Value;
        if (string.IsNullOrWhiteSpace(opt.ClientId) || string.IsNullOrWhiteSpace(opt.ClientSecret))
        {
            logger.LogWarning("Subscription:GrowthAuth is not configured; code redemption is unavailable.");
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
            logger.LogWarning(ex, "Could not acquire a Growth codes token for tenant {TenantId}.", tenantId);
            return null;
        }
    }
}
