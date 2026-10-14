using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;

namespace TaxVision.Auth.Api.Jobs;

/// <summary>
/// Cierra onboardings zombies: pestañas que quedaron en PendingPayment/PaymentProcessing/RegistrationPending
/// porque el buyer abandonó el checkout del provider (apretó "atrás" en el navegador). Cadencia e
/// <see cref="OnboardingExpirationOptions.TtlMinutes"/> configurables. Un correlationId por tick para
/// agrupar logs; scope propio por iteración; fallo del tick no tumba el servicio.
/// </summary>
public sealed class OnboardingExpirationJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IOptions<OnboardingExpirationOptions> options,
    ILogger<OnboardingExpirationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogInformation("Onboarding expiration job disabled; not scheduling.");
            return;
        }

        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        var interval = TimeSpan.FromMinutes(Math.Max(1, opt.IntervalMinutes));
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
                logger.LogError(ex, "Onboarding expiration tick failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
        using var _ = correlation.Push($"onboarding-expire-{Guid.NewGuid():N}");

        var processor = scope.ServiceProvider.GetRequiredService<OnboardingExpirationProcessor>();
        var expired = await processor.ProcessDueAsync(DateTime.UtcNow, ct);
        if (expired > 0)
            logger.LogInformation("Onboarding expiration: {Expired} onboarding(s) marked expired.", expired);
    }
}
