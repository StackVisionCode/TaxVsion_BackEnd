using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;

namespace TaxVision.Auth.Api.Jobs;

/// <summary>
/// Cada 5 minutos publica el email "completa tu oficina" para los onboardings pagados que no terminaron
/// in-session dentro de la ventana (OnboardingOptions.RegistrationReminderDelayMinutes). El recibo y el
/// carril $0 salen de inmediato desde OnboardingSuccessCompleter y no pasan por acá. Idempotente y seguro
/// con múltiples réplicas: el marcado RegistrationEmailSentAtUtc + RowVersion garantizan un solo email.
/// </summary>
public sealed class OnboardingRegistrationReminderScheduler(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<OnboardingRegistrationReminderScheduler> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var published = await RunOnceAsync(DateTime.UtcNow, stoppingToken);

                if (published > 0)
                    logger.LogInformation(
                        "Onboarding registration reminder: {Published} email(s) published.",
                        published
                    );
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Onboarding registration reminder tick failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Una unidad por onboarding: el marcado y el aviso se confirman juntos, o no queda ninguno.</summary>
    public async Task<int> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        IReadOnlyList<OnboardingRegistrationReadyIntegrationEvent> reminders;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<OnboardingRegistrationReminderProcessor>();
            reminders = await processor.PrepareDueAsync(nowUtc, ct);
        }

        var summary = await TransactionalJobUnit.RunEachAsync<
            OnboardingRegistrationReadyIntegrationEvent,
            AuthDbContext
        >(
            scopeFactory,
            reminders,
            (reminder, unit, token) =>
                unit
                    .Services.GetRequiredService<OnboardingRegistrationReminderProcessor>()
                    .MarkAndPublishAsync(reminder, unit.Outbox, nowUtc, token),
            logger,
            ct
        );
        return summary.Committed;
    }
}
