using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Offboarding;

namespace TaxVision.Auth.Api.Jobs;

/// <summary>
/// Barrido periódico del retiro con acuse: cada <see cref="OffboardingTrackingOptions.IntervalMinutes"/> marca
/// "Incomplete" los retiros que pasaron <see cref="OffboardingTrackingOptions.TimeoutMinutes"/> con pasos sin
/// acuse y avisa a la oficina (<c>UserOffboardingIncompleteIntegrationEvent</c>). Mismo molde que
/// <c>OnboardingOrphanDetectorJob</c>: scope y correlationId por tick; un tick fallido no tumba el servicio.
/// </summary>
public sealed class OffboardingRunsSweepJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IOptions<OffboardingTrackingOptions> options,
    ILogger<OffboardingRunsSweepJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogInformation("Offboarding runs sweep disabled; not scheduling.");
            return;
        }

        // Publica por Wolverine: espera a que la app arranque.
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(opt.IntervalMinutes));
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
                logger.LogError(ex, "Offboarding runs sweep tick failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
        using var _ = correlation.Push($"offboarding-sweep-{Guid.NewGuid():N}");

        var flagged = await scope
            .ServiceProvider.GetRequiredService<OffboardingRunSweeper>()
            .SweepAsync(DateTime.UtcNow, ct);
        if (flagged > 0)
            logger.LogWarning("Offboarding runs sweep: {Flagged} offboarding(s) flagged as incomplete.", flagged);
    }
}
