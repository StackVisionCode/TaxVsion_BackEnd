using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.RateLimiting;

public static class RateLimitingRegistration
{
    public static IServiceCollection AddTaxVisionGatewayRateLimiting(
        this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context =>
                {
                    var path = context.Request.Path.Value ?? string.Empty;
                    var isSensitiveAuthEndpoint =
                        path.Equals("/auth/login", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/auth/refresh", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals(
                            "/auth/invitations/accept",
                            StringComparison.OrdinalIgnoreCase) ||
                        path.Equals(
                            "/auth/invitations",
                            StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("/tenants", StringComparison.OrdinalIgnoreCase) &&
                        HttpMethods.IsPost(context.Request.Method);

                    if (!isSensitiveAuthEndpoint)
                    {
                        return RateLimitPartition.GetNoLimiter(
                            partitionKey: "unlimited");
                    }

                    var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: $"{client}:{path.ToLowerInvariant()}",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                });
        });

        return services;
    }
}
