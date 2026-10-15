using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;

/// <summary>OBF-2.7 — detector de onboardings huérfanos. Query al repo trae pagados sin registro
/// completado tras la ventana; publica un evento por cada uno. La dedup es responsabilidad del consumer
/// (Notification) — el detector solo garantiza una entrega por tick. Es puramente observacional: no
/// muta aggregates, no bloquea nada. Un fallo del publish por un onboarding no detiene el resto del
/// batch.</summary>
public sealed class OnboardingOrphanDetectorProcessor(
    ITenantOnboardingRepository onboardings,
    IMessageBus bus,
    ICorrelationContext correlation,
    IOptions<OnboardingOrphanDetectorOptions> options,
    ILogger<OnboardingOrphanDetectorProcessor> logger
)
{
    public async Task<int> ProcessDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        var opt = options.Value;
        var cutoff = nowUtc.AddHours(-opt.WindowHours);
        var orphans = await onboardings.GetOrphansAsync(cutoff, opt.BatchSize, ct);
        if (orphans.Count == 0)
            return 0;

        bus.TenantId = PlatformTenant.Id.ToString();
        var published = 0;
        foreach (var onboarding in orphans)
        {
            try
            {
                await bus.PublishAsync(
                    new OnboardingOrphanDetectedIntegrationEvent
                    {
                        OnboardingId = onboarding.Id,
                        Email = onboarding.Email,
                        FirstName = onboarding.FirstName,
                        LastName = onboarding.LastName,
                        PaymentCompletedAtUtc = onboarding.PaymentCompletedAtUtc ?? nowUtc,
                        CreatedAtUtc = onboarding.CreatedAtUtc,
                        GrossAmountCents = onboarding.GrossAmountCents ?? 0,
                        Currency = onboarding.Currency ?? "USD",
                        Status = onboarding.Status.ToString(),
                        CorrelationId = correlation.CorrelationId,
                    }
                );
                published++;
            }
            catch (Exception ex)
            {
                // Un fallo del publish por un onboarding no debe tumbar el resto del batch: el tick
                // siguiente volverá a listarlo mientras siga en estado huérfano.
                logger.LogError(
                    ex,
                    "Failed to publish OnboardingOrphanDetected for {OnboardingId}; will retry next tick.",
                    onboarding.Id
                );
            }
        }
        return published;
    }
}
