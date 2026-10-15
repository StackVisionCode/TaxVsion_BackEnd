using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;

namespace TaxVision.Auth.Api.Jobs;

/// <summary>OBF-2.7 — hosted service que corre el OnboardingOrphanDetectorProcessor cada
/// <see cref="OnboardingOrphanDetectorOptions.IntervalMinutes"/>. Mismo molde que OnboardingExpirationJob:
/// correlationId por tick, scope propio, fallo del tick no tumba el servicio.</summary>
public sealed class OnboardingOrphanDetectorJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IOptions<OnboardingOrphanDetectorOptions> options,
    ILogger<OnboardingOrphanDetectorJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogInformation("Onboarding orphan detector job disabled; not scheduling.");
            return;
        }

        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        var interval = TimeSpan.FromMinutes(Math.Max(5, opt.IntervalMinutes));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Onboarding orphan detector tick failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
        using var _ = correlation.Push($"onboarding-orphan-detect-{Guid.NewGuid():N}");

        var processor = scope.ServiceProvider.GetRequiredService<OnboardingOrphanDetectorProcessor>();
        var detected = await processor.ProcessDueAsync(DateTime.UtcNow, ct);
        if (detected > 0)
            logger.LogWarning("Onboarding orphan detector: {Detected} orphaned onboarding(s) published.", detected);
    }
}
