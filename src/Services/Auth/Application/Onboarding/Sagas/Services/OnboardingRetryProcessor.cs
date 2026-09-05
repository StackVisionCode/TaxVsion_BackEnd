using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.Failures;
using TaxVision.Auth.Application.Onboarding.Sagas.Commands;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.Sagas.Services;

public sealed class OnboardingRetryProcessor(
    ITenantOnboardingRepository onboardings,
    IUnitOfWork unitOfWork,
    IMessageBus bus,
    IOnboardingMetrics metrics
)
{
    private const int BatchSize = 50;
    private static readonly TimeSpan RetryDispatchLease = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
    ];

    public async Task<(int Resumed, int Exhausted, int Checked)> ProcessDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        var due = await onboardings.GetDueForRetryAsync(nowUtc, BatchSize, ct);
        if (due.Count == 0)
            return (0, 0, 0);

        var resumed = 0;
        var exhausted = 0;
        foreach (var onboarding in due)
        {
            var outcome = await ProcessOneAsync(onboarding, nowUtc, ct);
            resumed += outcome.Resumed ? 1 : 0;
            exhausted += outcome.Exhausted ? 1 : 0;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return (resumed, exhausted, due.Count);
    }

    private async Task<(bool Resumed, bool Exhausted)> ProcessOneAsync(
        TenantOnboarding onboarding,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (onboarding.FailedStep is not { } step || onboarding.FailureCode is not { } code)
            return (false, false);

        if (FailureClassifier.Classify(step, code) != FailureKind.Transient)
            return (false, MarkManualReview(onboarding, "Retry scheduler: step reclassified as non-retryable."));

        if (onboarding.RetryAttempt >= RetryDelays.Length)
        {
            return (
                false,
                MarkManualReview(onboarding, $"Retry scheduler: exhausted {RetryDelays.Length} automatic attempts.")
            );
        }

        if (onboarding.NextRetryAtUtc is null)
        {
            if (onboarding.ScheduleRetry(nowUtc.Add(RetryDelays[onboarding.RetryAttempt])).IsFailure)
                return (false, false);

            return (false, false);
        }

        if (onboarding.MarkRetryDispatched(nowUtc.Add(RetryDispatchLease)).IsFailure)
            return (false, false);

        await bus.PublishAsync(new ResumeOnboardingProvisioningCommand(onboarding.Id));
        return (true, false);
    }

    private bool MarkManualReview(TenantOnboarding onboarding, string reason)
    {
        if (onboarding.MarkManualReview(reason).IsFailure)
            return false;

        metrics.RecordManualReview();
        return true;
    }
}
