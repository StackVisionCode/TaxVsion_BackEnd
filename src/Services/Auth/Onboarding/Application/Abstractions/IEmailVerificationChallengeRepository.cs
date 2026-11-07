using TaxVision.Auth.Onboarding.Domain.EmailVerification;

namespace TaxVision.Auth.Onboarding.Application.Abstractions;

public interface IEmailVerificationChallengeRepository
{
    Task<EmailVerificationChallenge?> GetByIdAsync(Guid challengeId, CancellationToken ct = default);
    Task<IReadOnlyCollection<EmailVerificationChallenge>> ListActiveByEmailAsync(
        string email,
        DateTime nowUtc,
        CancellationToken ct = default
    );
    Task AddAsync(EmailVerificationChallenge challenge, CancellationToken ct = default);
}
