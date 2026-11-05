using System.Threading.RateLimiting;
using BuildingBlocks.Web.RateLimiting;

namespace TaxVision.Sms.Api.RateLimiting;

/// <summary>Límite por IP de los webhooks anónimos del proveedor.</summary>
public sealed class SmsWebhookRateLimitOptions
{
    public const string SectionName = "Sms:Webhooks:RateLimit";

    // Holgado: los avisos de entrega de una campaña llegan en ráfaga desde pocas IPs del proveedor.
    public int PermitLimit { get; set; } = 1000;
    public int WindowSeconds { get; set; } = 60;
}

public static class SmsWebhookRateLimiting
{
    public const string PolicyName = "sms-webhook";

    public static RateLimitPartition<string> Partition(HttpContext context, SmsWebhookRateLimitOptions options) =>
        TaxVisionRateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = TimeSpan.FromSeconds(options.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            }
        );
}
