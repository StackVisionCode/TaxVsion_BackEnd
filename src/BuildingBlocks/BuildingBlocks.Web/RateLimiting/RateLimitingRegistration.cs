using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Web.RateLimiting;

public static class RateLimitingRegistration
{
    /// <summary>
    /// Gate pre-auth por IP del Gateway (Capa 2). Las rutas y las cuotas se leen de
    /// <see cref="GatewayRateLimitOptions.SectionName"/>; si la sección no existe se usan los
    /// defaults de <see cref="GatewayRateLimitOptions"/>, que son el comportamiento histórico.
    /// </summary>
    public static IServiceCollection AddTaxVisionGatewayRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<GatewayRateLimitOptions>()
            .Bind(configuration.GetSection(GatewayRateLimitOptions.SectionName))
            .Validate(
                o => o.PreAuthByIp.PermitLimit > 0 && o.PreAuthByIp.WindowSeconds > 0,
                "GatewayRateLimiting:PreAuthByIp needs a positive PermitLimit and WindowSeconds."
            )
            .Validate(
                o => o.StorageUploadByTenant.PermitLimit > 0 && o.StorageUploadByTenant.WindowSeconds > 0,
                "GatewayRateLimiting:StorageUploadByTenant needs a positive PermitLimit and WindowSeconds."
            )
            .Validate(
                o =>
                    o.PreAuthByIp.Rules.Concat(o.StorageUploadByTenant.Rules)
                        .All(r => !string.IsNullOrWhiteSpace(r.Pattern)),
                "GatewayRateLimiting has a rule with an empty Pattern."
            );

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Resolución por petición y no capturada en una closure: así un cambio en caliente
                // de la configuración (IOptionsMonitor) surte efecto sin reiniciar el Gateway.
                var settings = context
                    .RequestServices.GetRequiredService<IOptionsMonitor<GatewayRateLimitOptions>>()
                    .CurrentValue;

                var path = context.Request.Path.Value ?? string.Empty;
                var method = context.Request.Method;

                if (settings.StorageUploadByTenant.Rules.Any(r => r.Matches(path, method)))
                {
                    // Por tenant, no por IP: una oficina entera detrás de un NAT comparte IP.
                    var tenant =
                        context.User.FindFirst("tenant_id")?.Value
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown";

                    return FixedWindow($"storage:{tenant}", settings.StorageUploadByTenant);
                }

                if (settings.PreAuthByIp.Rules.Any(r => r.Matches(path, method)))
                {
                    var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return FixedWindow($"{client}:{path.ToLowerInvariant()}", settings.PreAuthByIp);
                }

                // El resto pasa: su cuota real la aplica el [RateLimit] tiered de cada servicio.
                return RateLimitPartition.GetNoLimiter(partitionKey: "unlimited");
            });
        });

        return services;
    }

    private static RateLimitPartition<string> FixedWindow(string partitionKey, GatewayRateLimitGroup group) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = group.PermitLimit,
                Window = TimeSpan.FromSeconds(group.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            }
        );
}
