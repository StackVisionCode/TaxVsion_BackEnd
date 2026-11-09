using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Onboarding.Domain.EmailVerification;

namespace TaxVision.Auth.Onboarding.Infrastructure.Persistence.Repositories;

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
