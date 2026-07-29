using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Failures;
using TaxVision.Auth.Application.Onboarding.Sagas.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Jobs;

/// <summary>
/// PayFlow (Fase 17) — reintenta automáticamente los pasos de provisioning Transient
/// (<see cref="FailureClassifier"/>) con cadencia escalonada 5min/15min/1h. Tras agotar los 3
/// intentos, o si el fallo se reclasifica Permanent en el camino, pasa el onboarding a
/// ManualReview — "Permanent: sin retry, ManualReview inmediato" (plan Fase 17).
/// <para>
/// Simplificación deliberada frente al plan original: no implementa el burst inmediato de Polly
/// (1s/5s/30s) a nivel de HttpClient antes de caer a este scheduler — el tick de 1 minuto ya
/// atrapa cualquier fallo transient dentro de una ventana corta, y añadir Polly a cada M2M
/// HttpClient de Onboarding queda para una iteración futura si el volumen real lo justifica.
/// </para></summary>
public sealed class OnboardingRetryScheduler(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<OnboardingRetryScheduler> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
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
                logger.LogError(ex, "Onboarding retry scheduler tick failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var onboardings = scope.ServiceProvider.GetRequiredService<ITenantOnboardingRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var metrics = scope.ServiceProvider.GetRequiredService<IOnboardingMetrics>();

        var due = await onboardings.GetDueForRetryAsync(DateTime.UtcNow, batchSize: 50, ct);
        if (due.Count == 0)
            return;

        var resumed = 0;
        var exhausted = 0;
        foreach (var onboarding in due)
        {
            if (onboarding.FailedStep is not { } step || onboarding.FailureCode is not { } code)
                continue;

            if (FailureClassifier.Classify(step, code) != FailureKind.Transient)
            {
                onboarding.MarkManualReview("Retry scheduler: step reclassified as non-retryable.");
                metrics.RecordManualReview();
                exhausted++;
                continue;
            }

            if (onboarding.RetryAttempt >= RetryDelays.Length)
            {
                onboarding.MarkManualReview($"Retry scheduler: exhausted {RetryDelays.Length} automatic attempts.");
                metrics.RecordManualReview();
                exhausted++;
                continue;
            }

            if (onboarding.NextRetryAtUtc is null)
            {
                onboarding.ScheduleRetry(DateTime.UtcNow.Add(RetryDelays[onboarding.RetryAttempt]));
                continue;
            }

            if (onboarding.ResumeProvisioning().IsFailure)
                continue;

            await bus.PublishAsync(new ResumeOnboardingProvisioningCommand(onboarding.Id));
            resumed++;
        }

        await unitOfWork.SaveChangesAsync(ct);

        if (resumed > 0 || exhausted > 0)
            logger.LogInformation(
                "Onboarding retry scheduler: {Resumed} resumed, {Exhausted} sent to ManualReview, {Checked} checked.",
                resumed,
                exhausted,
                due.Count
            );
    }
}
