using TaxVision.Auth.Domain.Onboarding.EmailVerification;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

public interface IEmailVerificationChallengeRepository
{
    Task<EmailVerificationChallenge?> GetByIdAsync(Guid challengeId, CancellationToken ct = default);
    Task AddAsync(EmailVerificationChallenge challenge, CancellationToken ct = default);
}
