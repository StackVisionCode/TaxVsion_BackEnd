using TaxVision.Notification.Domain.Onboarding;

namespace TaxVision.Notification.Application.Abstractions;

public interface IOnboardingReceiptLookupRepository
{
    Task<OnboardingReceiptLookup?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default);
    Task AddAsync(OnboardingReceiptLookup lookup, CancellationToken ct = default);
}
