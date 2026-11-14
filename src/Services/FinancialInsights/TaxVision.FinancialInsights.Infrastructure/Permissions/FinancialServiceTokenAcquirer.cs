using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TaxVision.FinancialInsights.Infrastructure.Permissions;

public sealed class FinancialServiceTokenAcquirer(HttpClient http, IConfiguration configuration,
    ILogger<FinancialServiceTokenAcquirer> logger) : IServiceTokenAcquirer
{
    public async Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var clientId = configuration["ServiceAuthClient:ClientId"];
        var secret = configuration["ServiceAuthClient:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret)) return null;
        try
        {
            return (await http.RequestServiceTokenAsync(clientId, secret, tenantId, ct)).AccessToken;
        }
        catch (Exception ex) when (ex is ServiceTokenAcquisitionException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not acquire the FinancialInsights service token.");
            return null;
        }
    }
}
