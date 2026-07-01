using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Health;

public sealed class HttpEndpointHealthCheck(
    IHttpClientFactory httpClientFactory,
    string endpoint) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("taxvision-health");
            using var response = await client.GetAsync(endpoint, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy($"{endpoint} is healthy.")
                : HealthCheckResult.Unhealthy(
                    $"{endpoint} returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"{endpoint} is unavailable.", ex);
        }
    }
}
