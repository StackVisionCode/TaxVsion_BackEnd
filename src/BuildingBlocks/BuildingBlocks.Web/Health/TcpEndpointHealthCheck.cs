using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Health;

public sealed class TcpEndpointHealthCheck(string host, int port) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken);
            return HealthCheckResult.Healthy($"{host}:{port} is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{host}:{port} is not reachable.",
                ex);
        }
    }
}
