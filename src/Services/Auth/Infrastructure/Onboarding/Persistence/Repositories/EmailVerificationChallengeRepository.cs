using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.EmailVerification;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Infrastructure.Onboarding.Persistence.Repositories;

public sealed class EmailVerificationChallengeRepository(AuthDbContext db) : IEmailVerificationChallengeRepository
{
    public Task<EmailVerificationChallenge?> GetByIdAsync(Guid challengeId, CancellationToken ct = default) =>
        db.EmailVerificationChallenges.FirstOrDefaultAsync(challenge => challenge.Id == challengeId, ct);

    public async Task<IReadOnlyCollection<EmailVerificationChallenge>> ListActiveByEmailAsync(
        string email,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        var normalized = email?.Trim().ToLowerInvariant() ?? string.Empty;
        return await db
            .EmailVerificationChallenges.Where(challenge =>
                challenge.Email == normalized
                && challenge.SupersededAtUtc == null
                && challenge.VerifiedAtUtc == null
                && challenge.ExpiresAtUtc > nowUtc
            )
            .ToListAsync(ct);
    }

    public async Task AddAsync(EmailVerificationChallenge challenge, CancellationToken ct = default) =>
        await db.EmailVerificationChallenges.AddAsync(challenge, ct);
}
