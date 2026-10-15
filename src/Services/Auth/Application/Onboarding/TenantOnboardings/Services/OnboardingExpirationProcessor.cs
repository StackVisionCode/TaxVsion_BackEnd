using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;

/// <summary>
/// Marca Expired los onboardings sin cerrar cuya edad superó el TTL. El cierre del lado del provider
/// (Stripe/PayPal) no lo hace Auth: Stripe caduca su propia sesión y PayPal libera el pedido por su TTL.
/// Idempotente: <see cref="Domain.Onboarding.TenantOnboardings.TenantOnboarding.MarkExpired"/> es no-op
/// sobre un onboarding ya Expired.
/// </summary>
public sealed class OnboardingExpirationProcessor(
    ITenantOnboardingRepository onboardings,
    IUnitOfWork unitOfWork,
    IOptions<OnboardingExpirationOptions> options,
    ILogger<OnboardingExpirationProcessor> logger
)
{
    public async Task<int> ProcessDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        var opt = options.Value;
        var cutoff = nowUtc.AddMinutes(-opt.TtlMinutes);
        var due = await onboardings.GetExpiredPendingAsync(cutoff, opt.BatchSize, ct);
        if (due.Count == 0)
            return 0;

        var expired = 0;
        foreach (var onboarding in due)
        {
            // TODO best-effort provider cancel: cuando IPaymentAppOnboardingClient exponga Cancel.
            var result = onboarding.MarkExpired();
            if (result.IsSuccess)
                expired++;
            else
                logger.LogWarning(
                    "Onboarding {OnboardingId} skipped by expiration sweeper: {Error}.",
                    onboarding.Id,
                    result.Error.Message
                );
        }

        if (expired > 0)
            await unitOfWork.SaveChangesAsync(ct);
        return expired;
    }
}
