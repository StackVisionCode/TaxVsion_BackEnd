using TaxVision.Referrals.Domain.Attributions;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IOnboardingReferralRejectionRepository
{
    /// <summary>¿Growth ya descartó la declaración de referido de este alta? Acotada al tenant activo (el alta).</summary>
    Task<bool> ExistsAsync(Guid onboardingId, CancellationToken ct = default);

    Task AddAsync(OnboardingReferralRejection rejection, CancellationToken ct = default);
}
