using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Push;

/// <summary>
/// Degraded cuando <c>Notification:UseFcmPush</c> está encendido y FCM no inicializa. No envía nada.
/// </summary>
public sealed class FcmPushHealthCheck(IConfiguration configuration, IPushSender sender) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!configuration.GetValue<bool>("Notification:UseFcmPush"))
            return Task.FromResult(HealthCheckResult.Healthy("FCM push is turned off by configuration."));

        if (sender is FcmPushSender fcm && fcm.IsAvailable(out _))
            return Task.FromResult(HealthCheckResult.Healthy("FCM push is configured."));

        return Task.FromResult(HealthCheckResult.Degraded("Push provider is not configured."));
    }
}
