using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.RateLimiting;

public static class RateLimitingRegistration
{
    public static IServiceCollection AddTaxVisionGatewayRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                var isSensitiveAuthEndpoint =
                    path.Equals("/auth/login", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/auth/refresh", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/auth/mfa/verify", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/auth/password/forgot", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/auth/password/reset", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/auth/me/email/confirm", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/auth/invitations/accept", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/auth/invitations", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/tenants", StringComparison.OrdinalIgnoreCase)
                        && HttpMethods.IsPost(context.Request.Method);

                var isStorageUploadEndpoint =
                    HttpMethods.IsPost(context.Request.Method)
                    && (
                        path.Equals("/storage/files/uploads", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/storage/files/", StringComparison.OrdinalIgnoreCase)
                            && path.EndsWith("/complete", StringComparison.OrdinalIgnoreCase)
                    );

                if (!isSensitiveAuthEndpoint && !isStorageUploadEndpoint)
                {
                    return RateLimitPartition.GetNoLimiter(partitionKey: "unlimited");
                }

                var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (isStorageUploadEndpoint)
                {
                    var tenant = context.User.FindFirst("tenant_id")?.Value ?? client;
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: $"storage:{tenant}",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true,
                        }
                    );
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"{client}:{path.ToLowerInvariant()}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }
                );
            });
        });

        return services;
    }
}
